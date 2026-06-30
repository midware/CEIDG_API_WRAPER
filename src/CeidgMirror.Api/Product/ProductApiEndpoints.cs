using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CeidgMirror.Infrastructure.Postgres;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace CeidgMirror.Api;

public sealed class ProductApiOptions
{
    public const string SectionName = "ProductApi";
    public long FreeRegistrationTokens { get; init; } = 1000;
    public int DefaultPageSize { get; init; } = 25;
    public int MaxPageSize { get; init; } = 100;
}

public sealed record RegisterRequest(string Email, string Password, string? DisplayName);
public sealed record LoginRequest(string Email, string Password, string? KeyName);
public sealed record ApiKeyResponse(string ApiKey, string KeyPrefix, long TokenBalance);
public sealed record BalanceResponse(long TokenBalance);
public sealed record TokenPackageResponse(string Code, string Name, long Tokens, decimal NetPricePln);
public sealed record CompanySearchResponse(
    int Page,
    int PageSize,
    long TotalRows,
    int ReturnedRows,
    IReadOnlyList<string> Columns,
    long TokenCost,
    long TokenBalanceAfter,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Items);

public sealed record ApiUserContext(Guid UserId, string Email, long TokenBalance);

public static class ProductApiEndpoints
{
    public static IServiceCollection AddProductApi(this IServiceCollection services, IConfiguration configuration)
    {
        var postgresOptions = configuration.GetSection(PostgresOptions.SectionName).Get<PostgresOptions>() ?? new PostgresOptions();
        var productOptions = configuration.GetSection(ProductApiOptions.SectionName).Get<ProductApiOptions>() ?? new ProductApiOptions();

        services.AddSingleton(productOptions);
        services.AddSingleton(_ => NpgsqlDataSource.Create(postgresOptions.ConnectionString));
        services.AddSingleton<ProductApiStore>();
        return services;
    }

    public static WebApplication MapProductApi(this WebApplication app)
    {
        var auth = app.MapGroup("/auth").WithTags("Authentication");

        auth.MapPost("/register", async (RegisterRequest request, ProductApiStore store, ProductApiOptions options, CancellationToken cancellationToken) =>
        {
            var normalizedEmail = NormalizeEmail(request.Email);
            if (normalizedEmail is null)
            {
                return Results.BadRequest(new { error = "Email is required." });
            }

            if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 10)
            {
                return Results.BadRequest(new { error = "Password must contain at least 10 characters." });
            }

            try
            {
                var result = await store.RegisterUserAsync(
                    normalizedEmail,
                    request.Email.Trim(),
                    request.DisplayName,
                    PasswordSecurity.HashPassword(request.Password),
                    options.FreeRegistrationTokens,
                    cancellationToken);

                return Results.Ok(new ApiKeyResponse(result.ApiKey, result.KeyPrefix, result.TokenBalance));
            }
            catch (DuplicateEmailException)
            {
                return Results.Conflict(new { error = "User with this email already exists." });
            }
        })
        .WithSummary("Register a user and issue the first API key");

        auth.MapPost("/login", async (LoginRequest request, ProductApiStore store, CancellationToken cancellationToken) =>
        {
            var normalizedEmail = NormalizeEmail(request.Email);
            if (normalizedEmail is null || string.IsNullOrWhiteSpace(request.Password))
            {
                return Results.BadRequest(new { error = "Email and password are required." });
            }

            var user = await store.GetUserForLoginAsync(normalizedEmail, cancellationToken);
            if (user is null || !PasswordSecurity.VerifyPassword(request.Password, user.PasswordHash))
            {
                return Results.Unauthorized();
            }

            var apiKey = await store.CreateApiKeyAsync(user.UserId, request.KeyName ?? "login", cancellationToken);
            return Results.Ok(new ApiKeyResponse(apiKey.ApiKey, apiKey.KeyPrefix, user.TokenBalance));
        })
        .WithSummary("Login and issue a new API key");

        var account = app.MapGroup("/account").WithTags("Account");

        account.MapGet("/balance", async (HttpContext httpContext, ProductApiStore store, CancellationToken cancellationToken) =>
        {
            var user = await RequireApiUserAsync(httpContext, store, cancellationToken);
            return user is null ? Results.Unauthorized() : Results.Ok(new BalanceResponse(user.TokenBalance));
        })
        .WithSummary("Get current token balance");

        var billing = app.MapGroup("/billing").WithTags("Billing");

        billing.MapGet("/token-packages", () => Results.Ok(new[]
        {
            new TokenPackageResponse("starter", "Starter", 10_000, 49),
            new TokenPackageResponse("growth", "Growth", 100_000, 349),
            new TokenPackageResponse("scale", "Scale", 1_000_000, 2499)
        }))
        .WithSummary("List available token packages");

        var companies = app.MapGroup("/companies").WithTags("Companies");

        companies.MapGet("/columns", () => Results.Ok(CompanyColumnCatalog.Columns.Select(c => new
        {
            c.ApiName,
            c.Description,
            c.TokenWeight
        })))
        .WithSummary("List selectable company columns and token weights");

        companies.MapGet("", async (
            HttpContext httpContext,
            ProductApiStore store,
            ProductApiOptions options,
            [FromQuery] string? columns,
            [FromQuery] int page,
            [FromQuery] int pageSize,
            [FromQuery] string? nip,
            [FromQuery] string? regon,
            [FromQuery] string? name,
            [FromQuery] string? city,
            [FromQuery] string? status,
            [FromQuery] string? mainPkdCode,
            CancellationToken cancellationToken) =>
        {
            var user = await RequireApiUserAsync(httpContext, store, cancellationToken);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            var selectedColumns = CompanyColumnCatalog.Resolve(columns);
            if (selectedColumns.Count == 0)
            {
                return Results.BadRequest(new { error = "No valid columns selected.", availableColumns = CompanyColumnCatalog.Columns.Select(c => c.ApiName) });
            }

            page = page <= 0 ? 1 : page;
            pageSize = pageSize <= 0 ? options.DefaultPageSize : pageSize;
            pageSize = Math.Min(pageSize, options.MaxPageSize);

            var query = new CompanySearchQuery(page, pageSize, nip, regon, name, city, status, mainPkdCode, selectedColumns);
            var result = await store.SearchCompaniesAsync(user.UserId, query, cancellationToken);

            if (!result.Success)
            {
                return Results.Json(
                    new { error = "Insufficient token balance.", requiredTokens = result.TokenCost, currentBalance = user.TokenBalance },
                    statusCode: StatusCodes.Status402PaymentRequired);
            }

            return Results.Ok(new CompanySearchResponse(
                page,
                pageSize,
                result.TotalRows,
                result.Items.Count,
                selectedColumns.Select(c => c.ApiName).ToArray(),
                result.TokenCost,
                result.TokenBalanceAfter,
                result.Items));
        })
        .WithSummary("Search CEIDG companies with selectable columns and token billing");

        return app;
    }

    private static async Task<ApiUserContext?> RequireApiUserAsync(HttpContext httpContext, ProductApiStore store, CancellationToken cancellationToken)
    {
        if (!httpContext.Request.Headers.TryGetValue("X-Api-Key", out var values))
        {
            return null;
        }

        var apiKey = values.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        return await store.GetUserByApiKeyAsync(apiKey, cancellationToken);
    }

    private static string? NormalizeEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        return email.Trim().ToUpperInvariant();
    }
}

public sealed class ProductApiStore(NpgsqlDataSource dataSource)
{
    public async Task<(string ApiKey, string KeyPrefix, long TokenBalance)> RegisterUserAsync(
        string normalizedEmail,
        string email,
        string? displayName,
        string passwordHash,
        long freeTokens,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var userId = Guid.NewGuid();
        try
        {
            await using (var command = new NpgsqlCommand("""
                insert into app.api_users (id, email, email_normalized, password_hash, display_name, token_balance)
                values ($1, $2, $3, $4, $5, $6)
                """, connection, transaction))
            {
                command.Parameters.AddWithValue(userId);
                command.Parameters.AddWithValue(email);
                command.Parameters.AddWithValue(normalizedEmail);
                command.Parameters.AddWithValue(passwordHash);
                command.Parameters.AddWithValue((object?)displayName ?? DBNull.Value);
                command.Parameters.AddWithValue(freeTokens);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new DuplicateEmailException();
        }

        await InsertLedgerAsync(connection, transaction, userId, freeTokens, freeTokens, "registration_bonus", null, new { freeTokens }, cancellationToken);
        var key = await InsertApiKeyAsync(connection, transaction, userId, "registration", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (key.ApiKey, key.KeyPrefix, freeTokens);
    }

    public async Task<LoginUser?> GetUserForLoginAsync(string normalizedEmail, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            select id, password_hash, token_balance
            from app.api_users
            where email_normalized = $1 and disabled_at_utc is null
            """);
        command.Parameters.AddWithValue(normalizedEmail);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new LoginUser(reader.GetGuid(0), reader.GetString(1), reader.GetInt64(2));
    }

    public async Task<(string ApiKey, string KeyPrefix)> CreateApiKeyAsync(Guid userId, string keyName, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var key = await InsertApiKeyAsync(connection, transaction, userId, keyName, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return key;
    }

    public async Task<ApiUserContext?> GetUserByApiKeyAsync(string apiKey, CancellationToken cancellationToken)
    {
        var keyHash = ApiKeySecurity.HashApiKey(apiKey);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            select u.id, u.email, u.token_balance, k.id
            from app.api_keys k
            join app.api_users u on u.id = k.user_id
            where k.key_hash = $1
              and k.revoked_at_utc is null
              and u.disabled_at_utc is null
            """, connection, transaction);
        command.Parameters.AddWithValue(keyHash);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var user = new ApiUserContext(reader.GetGuid(0), reader.GetString(1), reader.GetInt64(2));
        var keyId = reader.GetGuid(3);
        await reader.DisposeAsync();

        await using var update = new NpgsqlCommand("update app.api_keys set last_used_at_utc = now() where id = $1", connection, transaction);
        update.Parameters.AddWithValue(keyId);
        await update.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return user;
    }

    public async Task<CompanySearchResult> SearchCompaniesAsync(Guid userId, CompanySearchQuery query, CancellationToken cancellationToken)
    {
        var where = new List<string>();
        var parameters = new List<NpgsqlParameter>();
        AddTextFilter(where, parameters, "nip", query.Nip, exact: true);
        AddTextFilter(where, parameters, "regon", query.Regon, exact: true);
        AddTextFilter(where, parameters, "name", query.Name, exact: false);
        AddTextFilter(where, parameters, "business_address_city", query.City, exact: false);
        AddTextFilter(where, parameters, "status", query.Status, exact: true);
        AddTextFilter(where, parameters, "main_pkd_code", query.MainPkdCode, exact: true);

        var whereSql = where.Count == 0 ? "" : " where " + string.Join(" and ", where);
        var selectSql = string.Join(", ", query.Columns.Select(c => c.SqlExpression + " as " + c.SqlAlias));
        var offset = (query.Page - 1) * query.PageSize;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        long totalRows;
        await using (var countCommand = new NpgsqlCommand("select count(*) from ceidg.company_records" + whereSql, connection))
        {
            foreach (var parameter in parameters)
            {
                countCommand.Parameters.Add(CloneParameter(parameter));
            }

            totalRows = (long)(await countCommand.ExecuteScalarAsync(cancellationToken) ?? 0L);
        }

        var items = new List<IReadOnlyDictionary<string, object?>>();
        await using (var dataCommand = new NpgsqlCommand($"""
            select {selectSql}
            from ceidg.company_records
            {whereSql}
            order by updated_at_utc desc, ceidg_id
            limit @limit offset @offset
            """, connection))
        {
            foreach (var parameter in parameters)
            {
                dataCommand.Parameters.Add(CloneParameter(parameter));
            }

            dataCommand.Parameters.AddWithValue("limit", query.PageSize);
            dataCommand.Parameters.AddWithValue("offset", offset);

            await using var reader = await dataCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < query.Columns.Count; i++)
                {
                    row[query.Columns[i].ApiName] = await reader.IsDBNullAsync(i, cancellationToken) ? null : reader.GetValue(i);
                }

                items.Add(row);
            }
        }

        var tokenCost = TokenPricing.CalculateCost(query.Columns, items.Count);
        var debit = await TryDebitAsync(userId, tokenCost, "company_search", new
        {
            query.Page,
            query.PageSize,
            ReturnedRows = items.Count,
            Columns = query.Columns.Select(c => c.ApiName).ToArray()
        }, cancellationToken);

        if (!debit.Success)
        {
            return CompanySearchResult.Insufficient(tokenCost, totalRows);
        }

        await using (var logCommand = dataSource.CreateCommand("""
            insert into app.api_query_log (id, user_id, endpoint, selected_columns, page, page_size, returned_rows, token_cost)
            values ($1, $2, 'GET /companies', $3, $4, $5, $6, $7)
            """))
        {
            logCommand.Parameters.AddWithValue(Guid.NewGuid());
            logCommand.Parameters.AddWithValue(userId);
            logCommand.Parameters.AddWithValue(query.Columns.Select(c => c.ApiName).ToArray());
            logCommand.Parameters.AddWithValue(query.Page);
            logCommand.Parameters.AddWithValue(query.PageSize);
            logCommand.Parameters.AddWithValue(items.Count);
            logCommand.Parameters.AddWithValue(tokenCost);
            await logCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        return CompanySearchResult.Ok(items, totalRows, tokenCost, debit.BalanceAfter);
    }

    private async Task<(bool Success, long BalanceAfter)> TryDebitAsync(Guid userId, long tokenCost, string reason, object metadata, CancellationToken cancellationToken)
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

        if (currentBalance < tokenCost)
        {
            await transaction.RollbackAsync(cancellationToken);
            return (false, currentBalance);
        }

        var balanceAfter = currentBalance - tokenCost;
        await using (var update = new NpgsqlCommand("update app.api_users set token_balance = $2, updated_at_utc = now() where id = $1", connection, transaction))
        {
            update.Parameters.AddWithValue(userId);
            update.Parameters.AddWithValue(balanceAfter);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        await InsertLedgerAsync(connection, transaction, userId, -tokenCost, balanceAfter, reason, Guid.NewGuid(), metadata, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (true, balanceAfter);
    }

    private static async Task InsertLedgerAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid userId,
        long delta,
        long balanceAfter,
        string reason,
        Guid? requestId,
        object metadata,
        CancellationToken cancellationToken)
    {
        await using var ledger = new NpgsqlCommand("""
            insert into app.token_ledger (user_id, delta, balance_after, reason, request_id, metadata)
            values ($1, $2, $3, $4, $5, $6::jsonb)
            """, connection, transaction);
        ledger.Parameters.AddWithValue(userId);
        ledger.Parameters.AddWithValue(delta);
        ledger.Parameters.AddWithValue(balanceAfter);
        ledger.Parameters.AddWithValue(reason);
        ledger.Parameters.AddWithValue((object?)requestId ?? DBNull.Value);
        ledger.Parameters.AddWithValue(JsonSerializer.Serialize(metadata));
        await ledger.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<(string ApiKey, string KeyPrefix)> InsertApiKeyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid userId,
        string keyName,
        CancellationToken cancellationToken)
    {
        var apiKey = ApiKeySecurity.GenerateApiKey();
        var prefix = ApiKeySecurity.GetPrefix(apiKey);
        await using var command = new NpgsqlCommand("""
            insert into app.api_keys (id, user_id, key_prefix, key_hash, name)
            values ($1, $2, $3, $4, $5)
            """, connection, transaction);
        command.Parameters.AddWithValue(Guid.NewGuid());
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(prefix);
        command.Parameters.AddWithValue(ApiKeySecurity.HashApiKey(apiKey));
        command.Parameters.AddWithValue(keyName);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return (apiKey, prefix);
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

    private static NpgsqlParameter CloneParameter(NpgsqlParameter parameter) => new(parameter.ParameterName, parameter.Value);
}

public sealed record LoginUser(Guid UserId, string PasswordHash, long TokenBalance);
public sealed record CompanySearchQuery(int Page, int PageSize, string? Nip, string? Regon, string? Name, string? City, string? Status, string? MainPkdCode, IReadOnlyList<CompanyColumn> Columns);

public sealed record CompanySearchResult(bool Success, IReadOnlyList<IReadOnlyDictionary<string, object?>> Items, long TotalRows, long TokenCost, long TokenBalanceAfter)
{
    public static CompanySearchResult Ok(IReadOnlyList<IReadOnlyDictionary<string, object?>> items, long totalRows, long tokenCost, long balanceAfter) =>
        new(true, items, totalRows, tokenCost, balanceAfter);

    public static CompanySearchResult Insufficient(long tokenCost, long totalRows) =>
        new(false, Array.Empty<IReadOnlyDictionary<string, object?>>(), totalRows, tokenCost, 0);
}

public sealed record CompanyColumn(string ApiName, string SqlExpression, string SqlAlias, string Description, int TokenWeight);

public static class CompanyColumnCatalog
{
    public static readonly IReadOnlyList<CompanyColumn> Columns = new[]
    {
        new CompanyColumn("ceidgId", "ceidg_id", "ceidg_id", "CEIDG entry identifier", 1),
        new CompanyColumn("nip", "nip", "nip", "Tax identification number", 1),
        new CompanyColumn("regon", "regon", "regon", "REGON number", 1),
        new CompanyColumn("name", "name", "name", "Business name", 1),
        new CompanyColumn("status", "status", "status", "Business status", 1),
        new CompanyColumn("ownerFirstName", "owner_first_name", "owner_first_name", "Owner first name", 1),
        new CompanyColumn("ownerLastName", "owner_last_name", "owner_last_name", "Owner last name", 1),
        new CompanyColumn("city", "business_address_city", "business_address_city", "Business city", 1),
        new CompanyColumn("voivodeship", "business_address_voivodeship", "business_address_voivodeship", "Business voivodeship", 1),
        new CompanyColumn("street", "business_address_street", "business_address_street", "Business street", 1),
        new CompanyColumn("postalCode", "business_address_postal_code", "business_address_postal_code", "Business postal code", 1),
        new CompanyColumn("mainPkdCode", "main_pkd_code", "main_pkd_code", "Main PKD code", 1),
        new CompanyColumn("phone", "phone", "phone", "Phone number", 3),
        new CompanyColumn("email", "email", "email", "Email address", 3),
        new CompanyColumn("website", "website", "website", "Website", 3),
        new CompanyColumn("pkdCodes", "pkd_codes::text", "pkd_codes", "All PKD codes as JSON", 4),
        new CompanyColumn("rawDetailPayload", "raw_detail_payload::text", "raw_detail_payload", "Full raw CEIDG detail JSON", 20)
    };

    private static readonly IReadOnlyDictionary<string, CompanyColumn> ByName = Columns.ToDictionary(c => c.ApiName, StringComparer.OrdinalIgnoreCase);
    private static readonly string[] DefaultColumns = { "ceidgId", "nip", "regon", "name", "status", "city", "mainPkdCode" };

    public static IReadOnlyList<CompanyColumn> Resolve(string? columns)
    {
        var requested = string.IsNullOrWhiteSpace(columns)
            ? DefaultColumns
            : columns.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return requested
            .Select(name => ByName.TryGetValue(name, out var column) ? column : null)
            .Where(column => column is not null)
            .Cast<CompanyColumn>()
            .DistinctBy(column => column.ApiName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

public static class TokenPricing
{
    public static long CalculateCost(IReadOnlyList<CompanyColumn> columns, int returnedRows)
    {
        var columnWeight = columns.Sum(column => column.TokenWeight);
        return 1 + (long)Math.Max(1, returnedRows) * columnWeight;
    }
}

public static class ApiKeySecurity
{
    public static string GenerateApiKey()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return "ceidg_" + Base64UrlEncode(bytes);
    }

    public static string GetPrefix(string apiKey) => apiKey.Length <= 16 ? apiKey : apiKey[..16];

    public static string HashApiKey(string apiKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));
        return Convert.ToHexString(hash);
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public static class PasswordSecurity
{
    private const int Iterations = 100_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public static string HashPassword(string password)
    {
        Span<byte> salt = stackalloc byte[SaltSize];
        RandomNumberGenerator.Fill(salt);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"pbkdf2-sha256:{Iterations}:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    public static bool VerifyPassword(string password, string storedHash)
    {
        var parts = storedHash.Split(':');
        if (parts.Length != 4 || parts[0] != "pbkdf2-sha256" || !int.TryParse(parts[1], out var iterations))
        {
            return false;
        }

        var salt = Convert.FromBase64String(parts[2]);
        var expectedHash = Convert.FromBase64String(parts[3]);
        var actualHash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expectedHash.Length);
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}

public sealed class DuplicateEmailException : Exception;
