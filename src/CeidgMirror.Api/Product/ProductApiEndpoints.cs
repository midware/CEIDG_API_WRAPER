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
    public long FreeRegistrationTokens { get; init; } = 5000;
    public int DefaultPageSize { get; init; } = 25;
    public int MaxPageSize { get; init; } = 100;
}

public sealed record RegisterRequest(string Email, string Password, string? DisplayName);
public sealed record LoginRequest(string Email, string Password, string? KeyName);
public sealed record ApiKeyResponse(string ApiKey, string KeyPrefix, long TokenBalance);
public sealed record BalanceResponse(long TokenBalance);
public sealed record TokenPackageResponse(string Code, string Name, long Tokens, decimal NetPricePln, decimal PlnPer1000Tokens, decimal TokensPerPln);
public sealed record AnalyticsSummaryResponse(long TotalCompanies, long ActiveCompanies, decimal ActivePercent, long SuspendedCompanies, decimal SuspendedPercent, long WithEmail, decimal WithEmailPercent, long WithPhone, decimal WithPhonePercent, long WithWebsite, decimal WithWebsitePercent, long TokenCost, long TokenBalanceAfter);
public sealed record AnalyticsBucketResponse(string Key, long Companies, long ActiveCompanies, decimal SharePercent);
public sealed record AnalyticsDistributionResponse(string Dimension, long TotalCompanies, int MinBucketSize, IReadOnlyList<AnalyticsBucketResponse> Buckets, long TokenCost, long TokenBalanceAfter);
public sealed record CompanySearchResponse(
    int Page,
    int PageSize,
    long TotalRows,
    int ReturnedRows,
    IReadOnlyList<string> Columns,
    long TokenCost,
    long TokenBalanceAfter,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Items);

public sealed record ApiUserContext(Guid UserId, Guid ApiKeyId, string Email, long TokenBalance);
public sealed record AccountPanel(
    Guid UserId,
    string Email,
    long TokenBalance,
    int ApiKeyCount,
    long QueryCount,
    IReadOnlyList<AccountApiKey> ApiKeys,
    IReadOnlyList<AccountLedgerEntry> Ledger,
    IReadOnlyList<AccountQueryLog> QueryLogs);

public sealed record AccountApiKey(Guid Id, string KeyPrefix, string? Name, DateTimeOffset CreatedAtUtc, DateTimeOffset? LastUsedAtUtc, DateTimeOffset? ExpiresAtUtc, DateTimeOffset? RevokedAtUtc);
public sealed record AccountLedgerEntry(DateTimeOffset CreatedAtUtc, long Delta, long BalanceAfter, string Reason, string? ApiKeyPrefix, string? ApiKeyName);
public sealed record AccountQueryLog(DateTimeOffset CreatedAtUtc, string Endpoint, IReadOnlyList<string> SelectedColumns, int ReturnedRows, long TokenCost);

public static class ProductApiEndpoints
{
    public static IServiceCollection AddProductApi(this IServiceCollection services, IConfiguration configuration)
    {
        var postgresOptions = configuration.GetSection(PostgresOptions.SectionName).Get<PostgresOptions>() ?? new PostgresOptions();
        var productOptions = configuration.GetSection(ProductApiOptions.SectionName).Get<ProductApiOptions>() ?? new ProductApiOptions();

        services.AddSingleton(productOptions);
        services.AddSingleton(_ => NpgsqlDataSource.Create(postgresOptions.ConnectionString));
        services.AddSingleton<ProductApiStore>();
        services.AddSingleton<ProductAnalyticsStore>();
        services.AddSingleton<ProductOperationsStore>();
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

            if (!user.EmailConfirmed)
            {
                return Results.Json(new { error = "Email address is not confirmed." }, statusCode: StatusCodes.Status403Forbidden);
            }

            var apiKey = await store.CreateApiKeyAsync(user.UserId, request.KeyName ?? "login", null, cancellationToken);
            return Results.Ok(new ApiKeyResponse(apiKey.ApiKey, apiKey.KeyPrefix, user.TokenBalance));
        })
        .WithSummary("Login and issue a new API key");

        var account = app.MapGroup("/account").WithTags("Account");

        account.MapGet("/balance", async ([FromHeader(Name = "X-Api-Key")] string? apiKey, ProductApiStore store, CancellationToken cancellationToken) =>
        {
            var user = await RequireApiUserAsync(apiKey, store, cancellationToken);
            return user is null ? Results.Unauthorized() : Results.Ok(new BalanceResponse(user.TokenBalance));
        })
        .WithSummary("Get current token balance");

        var billing = app.MapGroup("/billing").WithTags("Billing");

        billing.MapGet("/token-packages", () => Results.Ok(ProductPricing.TokenPackages))
        .WithSummary("List available token packages");

        billing.MapGet("/pricing", () => Results.Ok(new
        {
            Model = "package-row-cost",
            RequestBaseCost = 1,
            ReturnedRowCost = new
            {
                BasicProfile = 1,
                WithAnyContactColumn = 2,
                WithPkdCodes = 2,
                WithContactAndPkdCodes = 3,
                WithRawDetailPayloadExtra = 10
            },
            AnalyticsQueryCost = 25,
            Packages = ProductPricing.TokenPackages
        }))
        .WithSummary("Explain current token pricing model");

        var companies = app.MapGroup("/companies").WithTags("Companies");

        companies.MapGet("/columns", () => Results.Ok(CompanyColumnCatalog.Columns.Select(c => new
        {
            c.ApiName,
            c.Description,
            c.TokenWeight
        })))
        .WithSummary("List selectable company columns and token weights");

        companies.MapGet("", async (
            [FromHeader(Name = "X-Api-Key")] string? apiKey,
            ProductApiStore store,
            ProductApiOptions options,
            [FromQuery] string? columns,
            [FromQuery] int page,
            [FromQuery] int pageSize,
            [FromQuery] string? nip,
            [FromQuery] string? regon,
            [FromQuery] string? name,
            [FromQuery] string? country,
            [FromQuery] string? voivodeship,
            [FromQuery] string? county,
            [FromQuery] string? municipality,
            [FromQuery] string? city,
            [FromQuery] string? status,
            [FromQuery] string? mainPkdCode,
            [FromQuery] string? legalForm,
            [FromQuery] string? registrySource,
            [FromQuery] string? krsNumber,
            [FromQuery] bool? hasKrs,
            [FromQuery] bool? hasPhone,
            [FromQuery] bool? hasEmail,
            [FromQuery] bool? hasWebsite,
            [FromQuery] bool? includeHistory,
            CancellationToken cancellationToken) =>
        {
            var user = await RequireApiUserAsync(apiKey, store, cancellationToken);
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

            var query = new CompanySearchQuery(page, pageSize, nip, regon, name, country, voivodeship, county, municipality, city, status, mainPkdCode, legalForm, registrySource, krsNumber, hasKrs, hasPhone, hasEmail, hasWebsite, includeHistory == true, selectedColumns);
            var result = await store.SearchCompaniesAsync(user.UserId, user.ApiKeyId, query, cancellationToken);

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
        .WithSummary("Search CEIDG/KRS companies with selectable columns, filters and token billing")
        .WithDescription("Returns paginated current company records from the local mirror by default. Set includeHistory=true to include historical CEIDG/KRS entries for the same NIP/KRS/REGON identity. The columns query parameter accepts values from GET /companies/columns.")
        .Produces<CompanySearchResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status402PaymentRequired);


        var analytics = app.MapGroup("/analytics").WithTags("Analytics");

        analytics.MapGet("/summary", async (
            [FromHeader(Name = "X-Api-Key")] string? apiKey,
            ProductApiStore store,
            ProductAnalyticsStore analyticsStore,
            [FromQuery] string? voivodeship,
            [FromQuery] string? county,
            [FromQuery] string? municipality,
            [FromQuery] string? city,
            [FromQuery] string? status,
            [FromQuery] string? mainPkdCode,
            [FromQuery] string? pkdPrefix,
            [FromQuery] string? legalForm,
            [FromQuery] string? registrySource,
            [FromQuery] bool? hasKrs,
            [FromQuery] bool? hasEmail,
            [FromQuery] bool? hasPhone,
            [FromQuery] bool? hasWebsite,
            [FromQuery] DateOnly? startedFrom,
            [FromQuery] DateOnly? startedTo,
            CancellationToken cancellationToken) =>
        {
            var user = await RequireApiUserAsync(apiKey, store, cancellationToken);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            var filter = new AnalyticsFilter(voivodeship, county, municipality, city, status, mainPkdCode, pkdPrefix, registrySource, legalForm, hasKrs, hasEmail, hasPhone, hasWebsite, startedFrom, startedTo);
            var result = await analyticsStore.GetSummaryAsync(user.UserId, user.ApiKeyId, filter, cancellationToken);
            return result.Success
                ? Results.Ok(result.Data)
                : Results.Json(new { error = "Insufficient token balance.", requiredTokens = result.TokenCost, currentBalance = result.TokenBalanceAfter }, statusCode: StatusCodes.Status402PaymentRequired);
        })
        .WithSummary("Aggregate company market counts");

        analytics.MapGet("/distribution", async (
            [FromHeader(Name = "X-Api-Key")] string? apiKey,
            ProductApiStore store,
            ProductAnalyticsStore analyticsStore,
            [FromQuery] string dimension,
            [FromQuery] string? voivodeship,
            [FromQuery] string? county,
            [FromQuery] string? municipality,
            [FromQuery] string? city,
            [FromQuery] string? status,
            [FromQuery] string? mainPkdCode,
            [FromQuery] string? pkdPrefix,
            [FromQuery] string? legalForm,
            [FromQuery] string? registrySource,
            [FromQuery] bool? hasKrs,
            [FromQuery] bool? hasEmail,
            [FromQuery] bool? hasPhone,
            [FromQuery] bool? hasWebsite,
            [FromQuery] DateOnly? startedFrom,
            [FromQuery] DateOnly? startedTo,
            [FromQuery] int limit,
            [FromQuery] int minBucketSize,
            CancellationToken cancellationToken) =>
        {
            var allowedDimensions = new[] { "voivodeship", "county", "municipality", "city", "status", "mainPkdCode", "startedYear", "registeredYear", "sourceProfile", "legalForm" };
            if (string.IsNullOrWhiteSpace(dimension) || !allowedDimensions.Contains(dimension, StringComparer.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new { error = "Unsupported analytics dimension.", allowedDimensions });
            }

            var user = await RequireApiUserAsync(apiKey, store, cancellationToken);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            var filter = new AnalyticsFilter(voivodeship, county, municipality, city, status, mainPkdCode, pkdPrefix, registrySource, legalForm, hasKrs, hasEmail, hasPhone, hasWebsite, startedFrom, startedTo);
            var result = await analyticsStore.GetDistributionAsync(user.UserId, user.ApiKeyId, dimension, filter, limit, minBucketSize, cancellationToken);
            return result.Success
                ? Results.Ok(result.Data)
                : Results.Json(new { error = "Insufficient token balance.", requiredTokens = result.TokenCost, currentBalance = result.TokenBalanceAfter }, statusCode: StatusCodes.Status402PaymentRequired);
        })
        .WithSummary("Aggregate company distribution by selected dimension");

        var operations = app.MapGroup("/operations").WithTags("Operations");

        operations.MapGet("/data-quality", async (
            [FromHeader(Name = "X-Api-Key")] string? apiKey,
            ProductApiStore store,
            ProductOperationsStore operationsStore,
            CancellationToken cancellationToken) =>
        {
            var user = await RequireApiUserAsync(apiKey, store, cancellationToken);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            return Results.Ok(await operationsStore.GetDataQualityReportAsync(cancellationToken));
        })
        .WithSummary("Get data quality report for the mirrored company table")
        .WithDescription("Returns operational counts for missing identifiers, invalid countries, invalid phones, NIP history groups and duplicate current identifiers. This endpoint is informational and does not debit tokens.")
        .Produces<DataQualityReportResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized);

        operations.MapGet("/import-metrics", async (
            [FromHeader(Name = "X-Api-Key")] string? apiKey,
            ProductApiStore store,
            ProductOperationsStore operationsStore,
            CancellationToken cancellationToken) =>
        {
            var user = await RequireApiUserAsync(apiKey, store, cancellationToken);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            return Results.Ok(await operationsStore.GetImportMetricsAsync(cancellationToken));
        })
        .WithSummary("Get CEIDG/KRS import progress and throughput metrics")
        .WithDescription("Returns latest import run status, checkpoint counters, 24h failures and records-per-minute metrics for each import source. This endpoint is informational and does not debit tokens.")
        .Produces<ImportMetricsResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<ApiUserContext?> RequireApiUserAsync(string? apiKey, ProductApiStore store, CancellationToken cancellationToken)
    {
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
                insert into app.api_users (id, email, email_normalized, password_hash, display_name, token_balance, email_confirmed_at_utc)
                values ($1, $2, $3, $4, $5, $6, now())
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

        await InsertLedgerAsync(connection, transaction, userId, null, freeTokens, freeTokens, "registration_bonus", null, new { freeTokens }, cancellationToken);
        var key = await InsertApiKeyAsync(connection, transaction, userId, "registration", null, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (key.ApiKey, key.KeyPrefix, freeTokens);
    }

    public async Task<LoginUser?> GetUserForLoginAsync(string normalizedEmail, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            select id, password_hash, token_balance, email_confirmed_at_utc is not null
            from app.api_users
            where email_normalized = $1 and disabled_at_utc is null
            """);
        command.Parameters.AddWithValue(normalizedEmail);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new LoginUser(reader.GetGuid(0), reader.GetString(1), reader.GetInt64(2), reader.GetBoolean(3));
    }

    public async Task<(string ApiKey, string KeyPrefix, long TokenBalance)> RegisterUserAsync(
        string normalizedEmail,
        string email,
        string? displayName,
        string passwordHash,
        long freeTokens,
        string confirmationTokenHash,
        DateTimeOffset confirmationExpiresAtUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var userId = Guid.NewGuid();
        try
        {
            await using (var command = new NpgsqlCommand("""
                insert into app.api_users (id, email, email_normalized, password_hash, display_name, token_balance, email_confirmation_token_hash, email_confirmation_expires_at_utc)
                values ($1, $2, $3, $4, $5, $6, $7, $8)
                """, connection, transaction))
            {
                command.Parameters.AddWithValue(userId);
                command.Parameters.AddWithValue(email);
                command.Parameters.AddWithValue(normalizedEmail);
                command.Parameters.AddWithValue(passwordHash);
                command.Parameters.AddWithValue((object?)displayName ?? DBNull.Value);
                command.Parameters.AddWithValue(freeTokens);
                command.Parameters.AddWithValue(confirmationTokenHash);
                command.Parameters.AddWithValue(confirmationExpiresAtUtc);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new DuplicateEmailException();
        }

        await InsertLedgerAsync(connection, transaction, userId, null, freeTokens, freeTokens, "registration_bonus", null, new { freeTokens }, cancellationToken);
        var key = await InsertApiKeyAsync(connection, transaction, userId, "registration", null, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (key.ApiKey, key.KeyPrefix, freeTokens);
    }

    public async Task<bool> ConfirmEmailAsync(string confirmationTokenHash, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            update app.api_users
            set email_confirmed_at_utc = now(),
                email_confirmation_token_hash = null,
                email_confirmation_expires_at_utc = null,
                updated_at_utc = now()
            where email_confirmation_token_hash = $1
              and email_confirmation_expires_at_utc > now()
              and email_confirmed_at_utc is null
            """);
        command.Parameters.AddWithValue(confirmationTokenHash);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task MarkLoginAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            update app.api_users
            set last_login_at_utc = now(), updated_at_utc = now()
            where id = $1
            """);
        command.Parameters.AddWithValue(userId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
    public async Task<(string ApiKey, string KeyPrefix)> CreateApiKeyAsync(Guid userId, string keyName, DateTimeOffset? expiresAtUtc, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var key = await InsertApiKeyAsync(connection, transaction, userId, keyName, expiresAtUtc, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return key;
    }

    public async Task<bool> RevokeApiKeyAsync(Guid userId, Guid apiKeyId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            update app.api_keys
            set revoked_at_utc = now()
            where id = $1
              and user_id = $2
              and revoked_at_utc is null
            """);
        command.Parameters.AddWithValue(apiKeyId);
        command.Parameters.AddWithValue(userId);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<AccountPanel?> GetAccountPanelAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            select u.id,
                   u.email,
                   u.token_balance,
                   (select count(*)::int from app.api_keys k where k.user_id = u.id and k.revoked_at_utc is null and (k.expires_at_utc is null or k.expires_at_utc > now())) as api_key_count,
                   (select count(*)::bigint from app.api_query_log q where q.user_id = u.id) as query_count
            from app.api_users u
            where u.id = $1
              and u.disabled_at_utc is null
              and u.email_confirmed_at_utc is not null
            """);
        command.Parameters.AddWithValue(userId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var summary = new
        {
            UserId = reader.GetGuid(0),
            Email = reader.GetString(1),
            TokenBalance = reader.GetInt64(2),
            ApiKeyCount = reader.GetInt32(3),
            QueryCount = reader.GetInt64(4)
        };
        await reader.DisposeAsync();

        var apiKeys = new List<AccountApiKey>();
        await using (var keysCommand = dataSource.CreateCommand("""
            select id, key_prefix, name, created_at_utc, last_used_at_utc, expires_at_utc, revoked_at_utc
            from app.api_keys
            where user_id = $1
            order by created_at_utc desc
            limit 20
            """))
        {
            keysCommand.Parameters.AddWithValue(userId);
            await using var keysReader = await keysCommand.ExecuteReaderAsync(cancellationToken);
            while (await keysReader.ReadAsync(cancellationToken))
            {
                apiKeys.Add(new AccountApiKey(
                    keysReader.GetGuid(0),
                    keysReader.GetString(1),
                    await keysReader.IsDBNullAsync(2, cancellationToken) ? null : keysReader.GetString(2),
                    keysReader.GetFieldValue<DateTimeOffset>(3),
                    await keysReader.IsDBNullAsync(4, cancellationToken) ? null : keysReader.GetFieldValue<DateTimeOffset>(4),
                    await keysReader.IsDBNullAsync(5, cancellationToken) ? null : keysReader.GetFieldValue<DateTimeOffset>(5),
                    await keysReader.IsDBNullAsync(6, cancellationToken) ? null : keysReader.GetFieldValue<DateTimeOffset>(6)));
            }
        }

        var ledger = new List<AccountLedgerEntry>();
        await using (var ledgerCommand = dataSource.CreateCommand("""
            select l.created_at_utc, l.delta, l.balance_after, l.reason, k.key_prefix, k.name
            from app.token_ledger l
            left join app.api_keys k on k.id = l.api_key_id
            where l.user_id = $1
            order by l.created_at_utc desc
            limit 10
            """))
        {
            ledgerCommand.Parameters.AddWithValue(userId);
            await using var ledgerReader = await ledgerCommand.ExecuteReaderAsync(cancellationToken);
            while (await ledgerReader.ReadAsync(cancellationToken))
            {
                ledger.Add(new AccountLedgerEntry(
                    ledgerReader.GetFieldValue<DateTimeOffset>(0),
                    ledgerReader.GetInt64(1),
                    ledgerReader.GetInt64(2),
                    ledgerReader.GetString(3),
                    await ledgerReader.IsDBNullAsync(4, cancellationToken) ? null : ledgerReader.GetString(4),
                    await ledgerReader.IsDBNullAsync(5, cancellationToken) ? null : ledgerReader.GetString(5)));
            }
        }

        var queryLogs = new List<AccountQueryLog>();
        await using (var logsCommand = dataSource.CreateCommand("""
            select created_at_utc, endpoint, selected_columns, returned_rows, token_cost
            from app.api_query_log
            where user_id = $1
            order by created_at_utc desc
            limit 10
            """))
        {
            logsCommand.Parameters.AddWithValue(userId);
            await using var logsReader = await logsCommand.ExecuteReaderAsync(cancellationToken);
            while (await logsReader.ReadAsync(cancellationToken))
            {
                queryLogs.Add(new AccountQueryLog(
                    logsReader.GetFieldValue<DateTimeOffset>(0),
                    logsReader.GetString(1),
                    logsReader.GetFieldValue<string[]>(2),
                    logsReader.GetInt32(3),
                    logsReader.GetInt64(4)));
            }
        }

        return new AccountPanel(
            summary.UserId,
            summary.Email,
            summary.TokenBalance,
            summary.ApiKeyCount,
            summary.QueryCount,
            apiKeys,
            ledger,
            queryLogs);
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
              and (k.expires_at_utc is null or k.expires_at_utc > now())
              and u.disabled_at_utc is null
              and u.email_confirmed_at_utc is not null
            """, connection, transaction);
        command.Parameters.AddWithValue(keyHash);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var keyId = reader.GetGuid(3);
        var user = new ApiUserContext(reader.GetGuid(0), keyId, reader.GetString(1), reader.GetInt64(2));
        await reader.DisposeAsync();

        await using var update = new NpgsqlCommand("update app.api_keys set last_used_at_utc = now() where id = $1", connection, transaction);
        update.Parameters.AddWithValue(keyId);
        await update.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return user;
    }

    public async Task<CompanySearchResult> SearchCompaniesAsync(Guid userId, Guid apiKeyId, CompanySearchQuery query, CancellationToken cancellationToken)
    {
        var where = new List<string>();
        if (!query.IncludeHistory)
        {
            where.Add("is_current");
        }
        var parameters = new List<NpgsqlParameter>();
        AddTextFilter(where, parameters, "nip", query.Nip, exact: true);
        AddTextFilter(where, parameters, "regon", query.Regon, exact: true);
        AddTextFilter(where, parameters, "name", query.Name, exact: false);
        AddTextFilter(where, parameters, "business_address_country", query.Country, exact: true);
        AddTextFilter(where, parameters, "business_address_voivodeship", query.Voivodeship, exact: true);
        AddTextFilter(where, parameters, "business_address_county", query.County, exact: false);
        AddTextFilter(where, parameters, "business_address_municipality", query.Municipality, exact: false);
        AddTextFilter(where, parameters, "business_address_city", query.City, exact: false);
        AddTextFilter(where, parameters, "status", query.Status, exact: true);
        AddTextFilter(where, parameters, "main_pkd_code", query.MainPkdCode, exact: true);
        AddTextFilter(where, parameters, "legal_form", query.LegalForm, exact: true);
        AddRegistrySourceFilter(where, parameters, query.RegistrySource);
        AddTextFilter(where, parameters, "krs_number", query.KrsNumber, exact: true);
        AddKrsPresenceFilter(where, query.HasKrs);
        AddPresenceFilter(where, "phone", query.HasPhone);
        AddPresenceFilter(where, "email", query.HasEmail);
        AddPresenceFilter(where, "website", query.HasWebsite);

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
            order by is_current desc, current_rank asc, coalesce(ended_on, removed_on, suspended_on, resumed_on, registered_on, started_on, updated_at_utc::date) desc nulls last, updated_at_utc desc, ceidg_id
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
        var debit = await TryDebitAsync(userId, apiKeyId, tokenCost, "company_search", new
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
            insert into app.api_query_log (id, user_id, api_key_id, endpoint, selected_columns, page, page_size, returned_rows, token_cost)
            values ($1, $2, $3, 'GET /companies', $4, $5, $6, $7, $8)
            """))
        {
            logCommand.Parameters.AddWithValue(Guid.NewGuid());
            logCommand.Parameters.AddWithValue(userId);
            logCommand.Parameters.AddWithValue(apiKeyId);
            logCommand.Parameters.AddWithValue(query.Columns.Select(c => c.ApiName).ToArray());
            logCommand.Parameters.AddWithValue(query.Page);
            logCommand.Parameters.AddWithValue(query.PageSize);
            logCommand.Parameters.AddWithValue(items.Count);
            logCommand.Parameters.AddWithValue(tokenCost);
            await logCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        return CompanySearchResult.Ok(items, totalRows, tokenCost, debit.BalanceAfter);
    }

    private async Task<(bool Success, long BalanceAfter)> TryDebitAsync(Guid userId, Guid? apiKeyId, long tokenCost, string reason, object metadata, CancellationToken cancellationToken)
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

        await InsertLedgerAsync(connection, transaction, userId, apiKeyId, -tokenCost, balanceAfter, reason, Guid.NewGuid(), metadata, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (true, balanceAfter);
    }

    private static async Task InsertLedgerAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid userId,
        Guid? apiKeyId,
        long delta,
        long balanceAfter,
        string reason,
        Guid? requestId,
        object metadata,
        CancellationToken cancellationToken)
    {
        await using var ledger = new NpgsqlCommand("""
            insert into app.token_ledger (user_id, api_key_id, delta, balance_after, reason, request_id, metadata)
            values ($1, $2, $3, $4, $5, $6, $7::jsonb)
            """, connection, transaction);
        ledger.Parameters.AddWithValue(userId);
        ledger.Parameters.AddWithValue((object?)apiKeyId ?? DBNull.Value);
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
        DateTimeOffset? expiresAtUtc,
        CancellationToken cancellationToken)
    {
        var apiKey = ApiKeySecurity.GenerateApiKey();
        var prefix = ApiKeySecurity.GetPrefix(apiKey);
        await using var command = new NpgsqlCommand("""
            insert into app.api_keys (id, user_id, key_prefix, key_hash, name, expires_at_utc)
            values ($1, $2, $3, $4, $5, $6)
            """, connection, transaction);
        command.Parameters.AddWithValue(Guid.NewGuid());
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(prefix);
        command.Parameters.AddWithValue(ApiKeySecurity.HashApiKey(apiKey));
        command.Parameters.AddWithValue(keyName);
        command.Parameters.AddWithValue((object?)expiresAtUtc ?? DBNull.Value);
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

    private static void AddPresenceFilter(List<string> where, string columnName, bool? hasValue)
    {
        if (hasValue is null)
        {
            return;
        }

        var predicate = $"nullif(trim(coalesce({columnName}, '')), '') is not null";
        where.Add(hasValue.Value ? predicate : predicate.Replace(" is not null", " is null", StringComparison.Ordinal));
    }

    private static NpgsqlParameter CloneParameter(NpgsqlParameter parameter) => new(parameter.ParameterName, parameter.Value);
}

public sealed record LoginUser(Guid UserId, string PasswordHash, long TokenBalance, bool EmailConfirmed);
public sealed record CompanySearchQuery(int Page, int PageSize, string? Nip, string? Regon, string? Name, string? Country, string? Voivodeship, string? County, string? Municipality, string? City, string? Status, string? MainPkdCode, string? LegalForm, string? RegistrySource, string? KrsNumber, bool? HasKrs, bool? HasPhone, bool? HasEmail, bool? HasWebsite, bool IncludeHistory, IReadOnlyList<CompanyColumn> Columns);

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
        new CompanyColumn("statusNumber", "status_number", "status_number", "CEIDG status number", 1),
        new CompanyColumn("isCurrent", "is_current", "is_current", "Whether this row is the current record for the company identity", 1),
        new CompanyColumn("currentRank", "current_rank", "current_rank", "Current-row priority rank", 1),
        new CompanyColumn("legalForm", "legal_form", "legal_form", "Unified legal form", 1),
        new CompanyColumn("registeredOn", "registered_on", "registered_on", "Unified registration/start date", 1),
        new CompanyColumn("startedOn", "started_on", "started_on", "CEIDG business start date", 1),
        new CompanyColumn("suspendedOn", "suspended_on", "suspended_on", "CEIDG suspension date", 1),
        new CompanyColumn("endedOn", "ended_on", "ended_on", "CEIDG end date", 1),
        new CompanyColumn("removedOn", "removed_on", "removed_on", "CEIDG removal date", 1),
        new CompanyColumn("resumedOn", "resumed_on", "resumed_on", "CEIDG resume date", 1),
        new CompanyColumn("ownerFirstName", "owner_first_name", "owner_first_name", "Owner first name", 1),
        new CompanyColumn("ownerLastName", "owner_last_name", "owner_last_name", "Owner last name", 1),
        new CompanyColumn("ownerNip", "owner_nip", "owner_nip", "Owner NIP", 1),
        new CompanyColumn("ownerRegon", "owner_regon", "owner_regon", "Owner REGON", 1),
        new CompanyColumn("country", "business_address_country", "business_address_country", "Business country ISO-2 code", 1),
        new CompanyColumn("city", "business_address_city", "business_address_city", "Business city", 1),
        new CompanyColumn("voivodeship", "business_address_voivodeship", "business_address_voivodeship", "Business voivodeship", 1),
        new CompanyColumn("county", "business_address_county", "business_address_county", "Business county", 1),
        new CompanyColumn("municipality", "business_address_municipality", "business_address_municipality", "Business municipality", 1),
        new CompanyColumn("street", "business_address_street", "business_address_street", "Business street", 1),
        new CompanyColumn("building", "business_address_building", "business_address_building", "Business building number", 1),
        new CompanyColumn("unit", "business_address_unit", "business_address_unit", "Business unit number", 1),
        new CompanyColumn("postalCode", "business_address_postal_code", "business_address_postal_code", "Business postal code", 1),
        new CompanyColumn("terc", "business_address_terc", "business_address_terc", "TERC identifier", 1),
        new CompanyColumn("simc", "business_address_simc", "business_address_simc", "SIMC identifier", 1),
        new CompanyColumn("ulic", "business_address_ulic", "business_address_ulic", "ULIC identifier", 1),
        new CompanyColumn("mainPkdCode", "main_pkd_code", "main_pkd_code", "Main PKD code", 1),
        new CompanyColumn("registrySources", "registry_sources::text", "registry_sources", "Source registries", 1),
        new CompanyColumn("krsNumber", "krs_number", "krs_number", "KRS number", 1),
        new CompanyColumn("krsRegisterType", "krs_register_type", "krs_register_type", "KRS register type", 1),
        new CompanyColumn("krsCourtName", "krs_court_name", "krs_court_name", "KRS court", 1),
        new CompanyColumn("krsLastEntryDate", "krs_last_entry_date", "krs_last_entry_date", "KRS last entry date", 1),
        new CompanyColumn("krsUpdatedAtUtc", "krs_updated_at_utc", "krs_updated_at_utc", "Last KRS update timestamp", 1),
        new CompanyColumn("krsRepresentatives", "krs_representatives::text", "krs_representatives", "KRS representatives JSON", 4),
        new CompanyColumn("phone", "phone", "phone", "Normalized phone list", 3),
        new CompanyColumn("phoneMobile", "phone_mobile", "phone_mobile", "Normalized mobile phone numbers", 3),
        new CompanyColumn("phoneLandline", "phone_landline", "phone_landline", "Normalized landline phone numbers", 3),
        new CompanyColumn("phonesJson", "phones_json::text", "phones_json", "Structured phone numbers JSON", 4),
        new CompanyColumn("email", "email", "email", "Email address", 3),
        new CompanyColumn("website", "website", "website", "Website", 3),
        new CompanyColumn("electronicDeliveryAddress", "electronic_delivery_address", "electronic_delivery_address", "Electronic delivery address", 3),
        new CompanyColumn("otherContactForm", "other_contact_form", "other_contact_form", "Other contact form", 3),
        new CompanyColumn("pkdCodes", "pkd_codes::text", "pkd_codes", "All PKD codes as JSON", 4),
        new CompanyColumn("citizenships", "citizenships::text", "citizenships", "Citizenships JSON", 4),
        new CompanyColumn("correspondenceAddress", "correspondence_address::text", "correspondence_address", "Correspondence address JSON", 4),
        new CompanyColumn("additionalBusinessAddresses", "additional_business_addresses::text", "additional_business_addresses", "Additional business addresses JSON", 4),
        new CompanyColumn("permissions", "permissions::text", "permissions", "Permissions JSON", 4),
        new CompanyColumn("restrictions", "restrictions::text", "restrictions", "Restrictions JSON", 4),
        new CompanyColumn("sourceIndexUrl", "source_index_url", "source_index_url", "Source index URL", 1),
        new CompanyColumn("sourceDetailUrl", "source_detail_url", "source_detail_url", "Source detail URL", 1),
        new CompanyColumn("firstSeenAtUtc", "first_seen_at_utc", "first_seen_at_utc", "First seen timestamp", 1),
        new CompanyColumn("updatedAtUtc", "updated_at_utc", "updated_at_utc", "Last local update timestamp", 1),
        new CompanyColumn("rawIndexPayload", "raw_index_payload::text", "raw_index_payload", "Full raw CEIDG index JSON", 20),
        new CompanyColumn("rawDetailPayload", "raw_detail_payload::text", "raw_detail_payload", "Full raw CEIDG detail JSON", 20),
        new CompanyColumn("rawKrsPayload", "raw_krs_payload::text", "raw_krs_payload", "Full raw KRS JSON", 20)
    };

    private static readonly IReadOnlyDictionary<string, CompanyColumn> ByName = Columns.ToDictionary(c => c.ApiName, StringComparer.OrdinalIgnoreCase);
    private static readonly string[] DefaultColumns = { "ceidgId", "nip", "regon", "name", "status", "legalForm", "country", "city", "mainPkdCode", "registrySources" };


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
    private static readonly string[] ContactColumns = { "phone", "phoneMobile", "phoneLandline", "phonesJson", "email", "website", "electronicDeliveryAddress", "otherContactForm" };

    public static long CalculateCost(IReadOnlyList<CompanyColumn> columns, int returnedRows)
    {
        var rows = Math.Max(1, returnedRows);
        var selected = columns.Select(column => column.ApiName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var rowCost = 1L;
        if (ContactColumns.Any(selected.Contains))
        {
            rowCost += 1;
        }

        if (selected.Contains("pkdCodes"))
        {
            rowCost += 1;
        }

        if (selected.Contains("rawIndexPayload") || selected.Contains("rawDetailPayload") || selected.Contains("rawKrsPayload"))
        {
            rowCost += 10;
        }

        return 1 + rows * rowCost;
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
