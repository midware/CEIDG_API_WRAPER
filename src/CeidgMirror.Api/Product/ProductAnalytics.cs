using System.Text.Json;
using Npgsql;

namespace CeidgMirror.Api;

public static class ProductPricing
{
    public static readonly IReadOnlyList<TokenPackageResponse> TokenPackages =
    [
        Create("starter", "Starter", 50_000, 49),
        Create("growth", "Growth", 250_000, 149),
        Create("scale", "Scale", 1_000_000, 399),
        Create("enterprise", "Enterprise", 3_000_000, 999)
    ];

    private static TokenPackageResponse Create(string code, string name, long tokens, decimal price) =>
        new(code, name, tokens, price, Math.Round(price / tokens * 1000m, 4), Math.Round(tokens / price, 2));
}

public sealed record AnalyticsFilter(
    string? Voivodeship,
    string? County,
    string? Municipality,
    string? City,
    string? Status,
    string? MainPkdCode,
    string? PkdPrefix,
    string? RegistrySource,
    string? LegalForm,
    bool? HasKrs,
    bool? HasEmail,
    bool? HasPhone,
    bool? HasWebsite,
    DateOnly? StartedFrom,
    DateOnly? StartedTo);

public sealed record AnalyticsResult<T>(bool Success, T? Data, long TokenCost, long TokenBalanceAfter);

public sealed class ProductAnalyticsStore(NpgsqlDataSource dataSource)
{
    private const long AnalyticsTokenCost = 25;

    public async Task<AnalyticsResult<AnalyticsSummaryResponse>> GetSummaryAsync(
        Guid userId,
        Guid apiKeyId,
        AnalyticsFilter filter,
        CancellationToken cancellationToken)
    {
        var debit = await TryDebitAsync(userId, apiKeyId, "analytics_summary", new { filter }, cancellationToken);
        if (!debit.Success)
        {
            return new(false, null, AnalyticsTokenCost, debit.BalanceAfter);
        }

        var where = BuildWhere(filter, out var parameters);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand($"""
            select count(*)::bigint,
                   count(*) filter (where upper(coalesce(status, '')) in ('AKTYWNY', 'ACTIVE'))::bigint,
                   count(*) filter (where upper(coalesce(status, '')) like 'ZAWIESZ%')::bigint,
                   count(*) filter (where nullif(trim(coalesce(email, '')), '') is not null)::bigint,
                   count(*) filter (where nullif(trim(coalesce(phone, '')), '') is not null)::bigint,
                   count(*) filter (where nullif(trim(coalesce(website, '')), '') is not null)::bigint
            from ceidg.company_records
            {where}
            """, connection);
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(parameter);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var total = reader.GetInt64(0);
        var active = reader.GetInt64(1);
        var suspended = reader.GetInt64(2);
        var withEmail = reader.GetInt64(3);
        var withPhone = reader.GetInt64(4);
        var withWebsite = reader.GetInt64(5);

        await LogAnalyticsQueryAsync(userId, apiKeyId, "GET /analytics/summary", "summary", 1, AnalyticsTokenCost, cancellationToken);

        var response = new AnalyticsSummaryResponse(
            total,
            active,
            Percent(active, total),
            suspended,
            Percent(suspended, total),
            withEmail,
            Percent(withEmail, total),
            withPhone,
            Percent(withPhone, total),
            withWebsite,
            Percent(withWebsite, total),
            AnalyticsTokenCost,
            debit.BalanceAfter);

        return new(true, response, AnalyticsTokenCost, debit.BalanceAfter);
    }

    public async Task<AnalyticsResult<AnalyticsDistributionResponse>> GetDistributionAsync(
        Guid userId,
        Guid apiKeyId,
        string dimension,
        AnalyticsFilter filter,
        int limit,
        int minBucketSize,
        CancellationToken cancellationToken)
    {
        if (!AnalyticsDimensions.TryGetValue(dimension, out var expression))
        {
            return new(false, null, AnalyticsTokenCost, 0);
        }

        limit = Math.Clamp(limit <= 0 ? 20 : limit, 1, 100);
        minBucketSize = Math.Clamp(minBucketSize <= 0 ? 10 : minBucketSize, 1, 1000);

        var debit = await TryDebitAsync(userId, apiKeyId, "analytics_distribution", new { dimension, filter, limit, minBucketSize }, cancellationToken);
        if (!debit.Success)
        {
            return new(false, null, AnalyticsTokenCost, debit.BalanceAfter);
        }

        var where = BuildWhere(filter, out var parameters);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand($"""
            with filtered as (
                select {expression} as bucket_key, status
                from ceidg.company_records
                {where}
            ),
            totals as (
                select count(*)::bigint as total_count from filtered
            )
            select coalesce(nullif(bucket_key, ''), 'brak') as bucket_key,
                   count(*)::bigint as companies,
                   count(*) filter (where upper(coalesce(status, '')) in ('AKTYWNY', 'ACTIVE'))::bigint as active_companies,
                   totals.total_count
            from filtered
            cross join totals
            group by coalesce(nullif(bucket_key, ''), 'brak'), totals.total_count
            having count(*) >= @minBucketSize
            order by count(*) desc, bucket_key
            limit @limit
            """, connection);
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(parameter);
        }

        command.Parameters.AddWithValue("minBucketSize", minBucketSize);
        command.Parameters.AddWithValue("limit", limit);

        var buckets = new List<AnalyticsBucketResponse>();
        long total = 0;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            total = reader.GetInt64(3);
            var companies = reader.GetInt64(1);
            buckets.Add(new AnalyticsBucketResponse(
                reader.GetString(0),
                companies,
                reader.GetInt64(2),
                Percent(companies, total)));
        }

        await LogAnalyticsQueryAsync(userId, apiKeyId, "GET /analytics/distribution", "distribution:" + dimension, buckets.Count, AnalyticsTokenCost, cancellationToken);

        var response = new AnalyticsDistributionResponse(dimension, total, minBucketSize, buckets, AnalyticsTokenCost, debit.BalanceAfter);
        return new(true, response, AnalyticsTokenCost, debit.BalanceAfter);
    }

    private static readonly IReadOnlyDictionary<string, string> AnalyticsDimensions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["voivodeship"] = "business_address_voivodeship",
        ["county"] = "business_address_county",
        ["municipality"] = "business_address_municipality",
        ["city"] = "business_address_city",
        ["status"] = "status",
        ["mainPkdCode"] = "main_pkd_code",
        ["startedYear"] = "extract(year from started_on)::text",
        ["registeredYear"] = "extract(year from registered_on)::text",
        ["sourceProfile"] = "case when nullif(trim(coalesce(krs_number, '')), '') is not null and nullif(trim(coalesce(ceidg_id, '')), '') is not null then 'CEIDG+KRS' when nullif(trim(coalesce(krs_number, '')), '') is not null then 'KRS' else 'CEIDG' end",
        ["legalForm"] = "legal_form"
    };

    private static string BuildWhere(AnalyticsFilter filter, out List<NpgsqlParameter> parameters)
    {
        var where = new List<string> { "is_current" };
        parameters = [];

        AddTextFilter(where, parameters, "business_address_voivodeship", filter.Voivodeship, exact: true);
        AddTextFilter(where, parameters, "business_address_county", filter.County, exact: true);
        AddTextFilter(where, parameters, "business_address_municipality", filter.Municipality, exact: true);
        AddTextFilter(where, parameters, "business_address_city", filter.City, exact: false);
        AddTextFilter(where, parameters, "status", filter.Status, exact: true);
        AddTextFilter(where, parameters, "main_pkd_code", filter.MainPkdCode, exact: true);
        AddRegistrySourceFilter(where, parameters, filter.RegistrySource);
        AddTextFilter(where, parameters, "legal_form", filter.LegalForm, exact: true);
        AddKrsPresenceFilter(where, filter.HasKrs);

        if (!string.IsNullOrWhiteSpace(filter.PkdPrefix))
        {
            var parameterName = "p" + parameters.Count;
            where.Add($"main_pkd_code ilike @{parameterName}");
            parameters.Add(new NpgsqlParameter(parameterName, filter.PkdPrefix.Trim().TrimEnd('%') + "%"));
        }

        AddPresenceFilter(where, "email", filter.HasEmail);
        AddPresenceFilter(where, "phone", filter.HasPhone);
        AddPresenceFilter(where, "website", filter.HasWebsite);

        if (filter.StartedFrom is not null)
        {
            var parameterName = "p" + parameters.Count;
            where.Add($"started_on >= @{parameterName}");
            parameters.Add(new NpgsqlParameter(parameterName, filter.StartedFrom.Value.ToDateTime(TimeOnly.MinValue)));
        }

        if (filter.StartedTo is not null)
        {
            var parameterName = "p" + parameters.Count;
            where.Add($"started_on <= @{parameterName}");
            parameters.Add(new NpgsqlParameter(parameterName, filter.StartedTo.Value.ToDateTime(TimeOnly.MinValue)));
        }

        return where.Count == 0 ? string.Empty : "where " + string.Join(" and ", where);
    }

    private static void AddTextFilter(List<string> where, List<NpgsqlParameter> parameters, string column, string? value, bool exact)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var parameterName = "p" + parameters.Count;
        where.Add(exact ? $"upper({column}) = upper(@{parameterName})" : $"{column} ilike @{parameterName}");
        parameters.Add(new NpgsqlParameter(parameterName, exact ? value.Trim() : "%" + value.Trim() + "%"));
    }

    private static void AddRegistrySourceFilter(List<string> where, List<NpgsqlParameter> parameters, string? registrySource)
    {
        if (string.IsNullOrWhiteSpace(registrySource))
        {
            return;
        }

        var parameterName = "p" + parameters.Count;
        where.Add($"upper(@{parameterName}) = any(registry_sources)");
        parameters.Add(new NpgsqlParameter(parameterName, registrySource.Trim().ToUpperInvariant()));
    }

    private static void AddKrsPresenceFilter(List<string> where, bool? hasKrs)
    {
        if (hasKrs is null)
        {
            return;
        }

        where.Add(hasKrs.Value ? "nullif(trim(coalesce(krs_number, '')), '') is not null" : "nullif(trim(coalesce(krs_number, '')), '') is null");
    }

    private static void AddPresenceFilter(List<string> where, string column, bool? required)
    {
        if (required is null)
        {
            return;
        }

        var expression = $"nullif(trim(coalesce({column}, '')), '') is not null";
        where.Add(required.Value ? expression : "not (" + expression + ")");
    }

    private async Task<(bool Success, long BalanceAfter)> TryDebitAsync(Guid userId, Guid apiKeyId, string reason, object metadata, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        long currentBalance;
        await using (var select = new NpgsqlCommand("select token_balance from app.api_users where id = $1 for update", connection, transaction))
        {
            select.Parameters.AddWithValue(userId);
            var result = await select.ExecuteScalarAsync(cancellationToken);
            if (result is null)
            {
                return (false, 0);
            }

            currentBalance = (long)result;
        }

        if (currentBalance < AnalyticsTokenCost)
        {
            await transaction.RollbackAsync(cancellationToken);
            return (false, currentBalance);
        }

        var balanceAfter = currentBalance - AnalyticsTokenCost;
        await using (var update = new NpgsqlCommand("update app.api_users set token_balance = $2, updated_at_utc = now() where id = $1", connection, transaction))
        {
            update.Parameters.AddWithValue(userId);
            update.Parameters.AddWithValue(balanceAfter);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var ledger = new NpgsqlCommand("""
            insert into app.token_ledger (user_id, api_key_id, delta, balance_after, reason, request_id, metadata)
            values ($1, $2, $3, $4, $5, $6, $7::jsonb)
            """, connection, transaction))
        {
            ledger.Parameters.AddWithValue(userId);
            ledger.Parameters.AddWithValue(apiKeyId);
            ledger.Parameters.AddWithValue(-AnalyticsTokenCost);
            ledger.Parameters.AddWithValue(balanceAfter);
            ledger.Parameters.AddWithValue(reason);
            ledger.Parameters.AddWithValue(Guid.NewGuid());
            ledger.Parameters.AddWithValue(JsonSerializer.Serialize(metadata));
            await ledger.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return (true, balanceAfter);
    }

    private async Task LogAnalyticsQueryAsync(Guid userId, Guid apiKeyId, string endpoint, string selectedColumn, int returnedRows, long tokenCost, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            insert into app.api_query_log (id, user_id, api_key_id, endpoint, selected_columns, page, page_size, returned_rows, token_cost)
            values ($1, $2, $3, $4, $5, 1, $6, $7, $8)
            """);
        command.Parameters.AddWithValue(Guid.NewGuid());
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(apiKeyId);
        command.Parameters.AddWithValue(endpoint);
        command.Parameters.AddWithValue(new[] { selectedColumn });
        command.Parameters.AddWithValue(returnedRows);
        command.Parameters.AddWithValue(returnedRows);
        command.Parameters.AddWithValue(tokenCost);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static decimal Percent(long value, long total) =>
        total <= 0 ? 0m : Math.Round(value * 100m / total, 2);
}
