using System.Text.Json;
using Npgsql;

namespace CeidgMirror.Api;

public sealed record QualitySample(string Value, long Count);
public sealed record IdentityQuality(long MissingNip, long MissingRegon, long MissingNipAndRegon);
public sealed record AddressQuality(long InvalidCountryRows, long StreetWithPrefixRows, long MissingCityRows, IReadOnlyList<QualitySample> InvalidCountries);
public sealed record ContactQuality(long MissingPhoneRows, long InvalidPhoneRows, long MissingEmailRows, long InvalidEmailRows, long MissingWebsiteRows, IReadOnlyList<QualitySample> InvalidPhones);
public sealed record DuplicateQuality(long DuplicateNipGroups, long DuplicateNipRows, long DuplicateRegonGroups, long DuplicateRegonRows, long DuplicateKrsGroups, long DuplicateKrsRows);
public sealed record DataQualityReportResponse(DateTimeOffset GeneratedAtUtc, long TotalCompanies, IdentityQuality Identity, AddressQuality Address, ContactQuality Contact, DuplicateQuality Duplicates);

public sealed record ImportSkippedBreakdown(long ExistingCompanies, long NotFoundInRegister, long Other, string Explanation);

public sealed record ImportSourceMetrics(
    string ImportKind,
    DateTimeOffset? LastRunStartedAtUtc,
    DateTimeOffset? LastRunFinishedAtUtc,
    string? LastRunStatus,
    DateTimeOffset? LastCompletedRunFinishedAtUtc,
    DateTimeOffset? LastSuccessfulCheckpointAtUtc,
    DateTimeOffset? LastCheckpointAtUtc,
    long RunningRuns,
    long FailedRuns24h,
    long ImportedFromCheckpoints,
    long SkippedFromCheckpoints,
    ImportSkippedBreakdown SkippedBreakdown,
    long FailedFromCheckpoints,
    long? LastRunImported,
    long? LastRunSkipped,
    long? LastRunFailed,
    long? LastRunThrottled,
    decimal? LastRunRecordsPerMinute,
    bool HasIncompleteCheckpoint);

public sealed record ImportMetricsResponse(DateTimeOffset GeneratedAtUtc, IReadOnlyList<ImportSourceMetrics> Sources);

public sealed class ProductOperationsStore(NpgsqlDataSource dataSource)
{
    private const string ValidPhoneListPattern = "^(\\+48[0-9]{9}|\\+48 [0-9]{2} [0-9]{3} [0-9]{2} [0-9]{2}|\\+[0-9]{10,15})(, (\\+48[0-9]{9}|\\+48 [0-9]{2} [0-9]{3} [0-9]{2} [0-9]{2}|\\+[0-9]{10,15}))*$";

    public async Task<DataQualityReportResponse> GetDataQualityReportAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            select count(*)::bigint,
                   count(*) filter (where nullif(trim(coalesce(nip, '')), '') is null)::bigint,
                   count(*) filter (where nullif(trim(coalesce(regon, '')), '') is null)::bigint,
                   count(*) filter (where nullif(trim(coalesce(nip, '')), '') is null and nullif(trim(coalesce(regon, '')), '') is null)::bigint,
                   count(*) filter (where nullif(trim(coalesce(business_address_country, '')), '') is not null and business_address_country !~ '^[A-Z]{2}$')::bigint,
                   count(*) filter (where business_address_street ~* '^(ul\.|ulica\s+)')::bigint,
                   count(*) filter (where nullif(trim(coalesce(business_address_city, '')), '') is null)::bigint,
                   count(*) filter (where nullif(trim(coalesce(phone, '')), '') is null)::bigint,
                   count(*) filter (where nullif(trim(coalesce(phone, '')), '') is not null and phone !~ @phonePattern)::bigint,
                   count(*) filter (where nullif(trim(coalesce(email, '')), '') is null)::bigint,
                   count(*) filter (where nullif(trim(coalesce(email, '')), '') is not null and email !~* '^[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}$')::bigint,
                   count(*) filter (where nullif(trim(coalesce(website, '')), '') is null)::bigint
            from ceidg.company_records
            """, connection);
        command.Parameters.AddWithValue("phonePattern", ValidPhoneListPattern);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var total = reader.GetInt64(0);
        var identity = new IdentityQuality(reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3));
        var invalidCountryRows = reader.GetInt64(4);
        var streetPrefixRows = reader.GetInt64(5);
        var missingCityRows = reader.GetInt64(6);
        var missingPhoneRows = reader.GetInt64(7);
        var invalidPhoneRows = reader.GetInt64(8);
        var missingEmailRows = reader.GetInt64(9);
        var invalidEmailRows = reader.GetInt64(10);
        var missingWebsiteRows = reader.GetInt64(11);
        await reader.DisposeAsync();

        var duplicates = await GetDuplicateQualityAsync(connection, cancellationToken);
        var invalidCountries = await GetSamplesAsync(connection, "business_address_country", "nullif(trim(coalesce(business_address_country, '')), '') is not null and business_address_country !~ '^[A-Z]{2}$'", null, cancellationToken);
        var invalidPhones = await GetSamplesAsync(connection, "phone", "nullif(trim(coalesce(phone, '')), '') is not null and phone !~ @phonePattern", ValidPhoneListPattern, cancellationToken);

        return new DataQualityReportResponse(
            DateTimeOffset.UtcNow,
            total,
            identity,
            new AddressQuality(invalidCountryRows, streetPrefixRows, missingCityRows, invalidCountries),
            new ContactQuality(missingPhoneRows, invalidPhoneRows, missingEmailRows, invalidEmailRows, missingWebsiteRows, invalidPhones),
            duplicates);
    }

    public async Task<ImportMetricsResponse> GetImportMetricsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var importKinds = new List<string>();
        await using (var kindsCommand = new NpgsqlCommand("""
            select import_kind from source.import_run
            union
            select import_kind from source.import_checkpoint
            order by import_kind
            """, connection))
        await using (var kindsReader = await kindsCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await kindsReader.ReadAsync(cancellationToken))
            {
                importKinds.Add(kindsReader.GetString(0));
            }
        }

        var sources = new List<ImportSourceMetrics>();
        foreach (var importKind in importKinds)
        {
            sources.Add(await GetSourceMetricsAsync(connection, importKind, cancellationToken));
        }

        return new ImportMetricsResponse(DateTimeOffset.UtcNow, sources);
    }

    private static async Task<DuplicateQuality> GetDuplicateQualityAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            with nip_dupes as (
                select count(*)::bigint as rows_count from ceidg.company_records where nullif(trim(coalesce(nip, '')), '') is not null group by nip having count(*) > 1
            ), regon_dupes as (
                select count(*)::bigint as rows_count from ceidg.company_records where nullif(trim(coalesce(regon, '')), '') is not null group by regon having count(*) > 1
            ), krs_dupes as (
                select count(*)::bigint as rows_count from ceidg.company_records where nullif(trim(coalesce(krs_number, '')), '') is not null group by krs_number having count(*) > 1
            )
            select coalesce((select count(*) from nip_dupes), 0)::bigint,
                   coalesce((select sum(rows_count) from nip_dupes), 0)::bigint,
                   coalesce((select count(*) from regon_dupes), 0)::bigint,
                   coalesce((select sum(rows_count) from regon_dupes), 0)::bigint,
                   coalesce((select count(*) from krs_dupes), 0)::bigint,
                   coalesce((select sum(rows_count) from krs_dupes), 0)::bigint
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new DuplicateQuality(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5));
    }

    private static async Task<IReadOnlyList<QualitySample>> GetSamplesAsync(NpgsqlConnection connection, string column, string predicate, string? phonePattern, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand($"""
            select {column}, count(*)::bigint
            from ceidg.company_records
            where {predicate}
            group by {column}
            order by count(*) desc, {column}
            limit 10
            """, connection);
        if (phonePattern is not null)
        {
            command.Parameters.AddWithValue("phonePattern", phonePattern);
        }

        var samples = new List<QualitySample>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            samples.Add(new QualitySample(reader.IsDBNull(0) ? "" : reader.GetString(0), reader.GetInt64(1)));
        }

        return samples;
    }

    private static async Task<ImportSourceMetrics> GetSourceMetricsAsync(NpgsqlConnection connection, string importKind, CancellationToken cancellationToken)
    {
        ImportRunSnapshot? lastRun = null;
        await using (var runCommand = new NpgsqlCommand("""
            select started_at_utc, finished_at_utc, status, details
            from source.import_run
            where import_kind = $1
            order by started_at_utc desc
            limit 1
            """, connection))
        {
            runCommand.Parameters.AddWithValue(importKind);
            await using var runReader = await runCommand.ExecuteReaderAsync(cancellationToken);
            if (await runReader.ReadAsync(cancellationToken))
            {
                lastRun = new ImportRunSnapshot(
                    runReader.GetFieldValue<DateTimeOffset>(0),
                    runReader.IsDBNull(1) ? null : runReader.GetFieldValue<DateTimeOffset>(1),
                    runReader.GetString(2),
                    runReader.GetString(3));
            }
        }

        await using var command = new NpgsqlCommand("""
            select (select max(finished_at_utc) from source.import_run where import_kind = $1 and status = 'completed'),
                   (select count(*)::bigint from source.import_run where import_kind = $1 and status = 'running'),
                   (select count(*)::bigint from source.import_run where import_kind = $1 and status not in ('completed', 'partial', 'running') and started_at_utc >= now() - interval '24 hours'),
                   (select coalesce(sum(imported_count), 0)::bigint from source.import_checkpoint where import_kind = $1),
                   (select coalesce(sum(skipped_count), 0)::bigint from source.import_checkpoint where import_kind = $1),
                   (select coalesce(sum(failed_count), 0)::bigint from source.import_checkpoint where import_kind = $1),
                   (select max(updated_at_utc) from source.import_checkpoint where import_kind = $1 and imported_count > 0),
                   (select max(updated_at_utc) from source.import_checkpoint where import_kind = $1),
                   (select coalesce(bool_or(not completed), false) from source.import_checkpoint where import_kind = $1)
            """, connection);
        command.Parameters.AddWithValue(importKind);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        DateTimeOffset? lastCompletedRun = reader.IsDBNull(0) ? null : reader.GetFieldValue<DateTimeOffset>(0);
        var runningRuns = reader.GetInt64(1);
        var failedRuns24h = reader.GetInt64(2);
        var imported = reader.GetInt64(3);
        var skipped = reader.GetInt64(4);
        var failed = reader.GetInt64(5);
        DateTimeOffset? lastSuccessfulCheckpoint = reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6);
        DateTimeOffset? lastCheckpoint = reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7);
        var hasIncompleteCheckpoint = !reader.IsDBNull(8) && reader.GetBoolean(8);

        var details = lastRun is null ? null : ParseDetails(lastRun.DetailsJson);
        var recordsPerMinute = CalculateRecordsPerMinute(lastRun, details?.Imported);

        return new ImportSourceMetrics(
            importKind,
            lastRun?.StartedAtUtc,
            lastRun?.FinishedAtUtc,
            lastRun?.Status,
            lastCompletedRun,
            lastSuccessfulCheckpoint,
            lastCheckpoint,
            runningRuns,
            failedRuns24h,
            imported,
            skipped,
            BuildSkippedBreakdown(importKind, skipped),
            failed,
            details?.Imported,
            details?.Skipped,
            details?.Failed,
            details?.Throttled,
            recordsPerMinute,
            hasIncompleteCheckpoint);
    }

    private static ImportSkippedBreakdown BuildSkippedBreakdown(string importKind, long skipped)
    {
        if (string.Equals(importKind, "changes-detail", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(importKind, "initial-index-detail", StringComparison.OrdinalIgnoreCase))
        {
            return new ImportSkippedBreakdown(skipped, 0, 0, "CEIDG: rekord był już w bazie i SkipExistingCompanies=true, więc nie pobierano szczegółów ponownie.");
        }

        if (string.Equals(importKind, "krs-current-excerpt", StringComparison.OrdinalIgnoreCase))
        {
            return new ImportSkippedBreakdown(0, skipped, 0, "KRS: numer wystąpił w biuletynie, ale aktualny odpis nie istnieje w skonfigurowanym rejestrze.");
        }

        return new ImportSkippedBreakdown(0, 0, skipped, "Kontrolowane pominięcia bez szczegółowego powodu dla tego typu importu.");
    }
    private static ImportRunDetails ParseDetails(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return new ImportRunDetails(
            ReadLong(root, "imported"),
            ReadLong(root, "skipped"),
            ReadLong(root, "failed"),
            ReadLong(root, "throttled"));
    }

    private static long? ReadLong(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var result)
            ? result
            : null;

    private static decimal? CalculateRecordsPerMinute(ImportRunSnapshot? run, long? imported)
    {
        if (run?.FinishedAtUtc is null || imported is null || imported <= 0)
        {
            return null;
        }

        var minutes = (run.FinishedAtUtc.Value - run.StartedAtUtc).TotalMinutes;
        return minutes <= 0 ? null : Math.Round((decimal)imported.Value / (decimal)minutes, 2);
    }

    private sealed record ImportRunSnapshot(DateTimeOffset StartedAtUtc, DateTimeOffset? FinishedAtUtc, string Status, string DetailsJson);
    private sealed record ImportRunDetails(long? Imported, long? Skipped, long? Failed, long? Throttled);
}