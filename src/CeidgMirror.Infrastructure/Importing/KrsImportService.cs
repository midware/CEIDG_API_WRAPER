using System.Net;
using System.Security.Cryptography;
using System.Text;
using CeidgMirror.Application.Abstractions;
using CeidgMirror.Application.Importing;
using CeidgMirror.Contracts;
using CeidgMirror.Infrastructure.Krs;
using Microsoft.Extensions.Logging;

namespace CeidgMirror.Infrastructure.Importing;

public sealed class KrsImportService(
    IKrsClient client,
    ICompanyRecordStore store,
    KrsImportOptions options,
    ILogger<KrsImportService> logger) : IKrsImportService
{
    public async Task RunKrsImportAsync(CancellationToken cancellationToken = default)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("KRS import is disabled.");
            return;
        }

        var importRunId = await store.StartImportRunAsync("krs-current-excerpt", cancellationToken);
        var summary = KrsImportSummary.Empty;

        try
        {
            summary = string.Equals(options.Source, "Bulletin", StringComparison.OrdinalIgnoreCase)
                ? await RunBulletinImportAsync(importRunId, cancellationToken)
                : await RunSeededImportAsync(importRunId, cancellationToken);

            await store.CompleteImportRunAsync(importRunId, summary.Completed ? "completed" : "partial", new
            {
                options.Source,
                options.Register,
                options.MaxItems,
                imported = summary.Imported,
                skipped = summary.Skipped,
                failed = summary.Failed,
                throttled = summary.Throttled,
                processed = summary.Processed,
                total = summary.Total,
                completed = summary.Completed
            }, cancellationToken);
        }
        catch
        {
            await store.CompleteImportRunAsync(importRunId, "failed", new
            {
                options.Source,
                options.Register,
                imported = summary.Imported,
                skipped = summary.Skipped,
                failed = summary.Failed,
                throttled = summary.Throttled,
                processed = summary.Processed,
                total = summary.Total,
                completed = false
            }, cancellationToken);
            throw;
        }
    }

    private async Task<KrsImportSummary> RunSeededImportAsync(Guid importRunId, CancellationToken cancellationToken)
    {
        var krsNumbers = options.SeedKrsNumbers
            .Select(NormalizeKrsNumber)
            .Where(number => !string.IsNullOrWhiteSpace(number))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(number => number, StringComparer.Ordinal)
            .ToArray();

        logger.LogInformation("KRS seeded source resolved {Count} numbers.", krsNumbers.Length);
        return await ProcessKrsNumbersAsync(
            importRunId,
            BuildSeededCheckpointKey(krsNumbers),
            options.StartDate,
            options.EndDate,
            krsNumbers,
            options.MaxItems,
            cancellationToken);
    }

    private async Task<KrsImportSummary> RunBulletinImportAsync(Guid importRunId, CancellationToken cancellationToken)
    {
        var endDate = options.EndDate ?? DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var totalSummary = KrsImportSummary.Empty;
        var completed = true;

        logger.LogInformation("KRS streaming bulletin import started for {StartDate:yyyy-MM-dd}..{EndDate:yyyy-MM-dd}.", options.StartDate, endDate);

        for (var day = options.StartDate; day <= endDate; day = day.AddDays(1))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var remaining = options.MaxItems <= 0 ? int.MaxValue : Math.Max(0, options.MaxItems - (int)Math.Min(int.MaxValue, totalSummary.Processed));
            if (remaining <= 0)
            {
                completed = false;
                logger.LogInformation("KRS MaxItems={MaxItems} reached. Stopping bulletin pass at {Day:yyyy-MM-dd}.", options.MaxItems, day);
                break;
            }

            var checkpointKey = BuildBulletinDayCheckpointKey(day);
            if (options.Resume)
            {
                var dayCheckpoint = await store.GetCheckpointAsync(checkpointKey, cancellationToken);
                if (dayCheckpoint?.Completed == true)
                {
                    logger.LogInformation("Skipping completed KRS bulletin day {Day:yyyy-MM-dd}.", day);
                    continue;
                }
            }

            logger.LogInformation("Requesting KRS bulletin for {Day:yyyy-MM-dd}.", day);
            var response = await client.GetBulletinAsync(day, options.DayFormat, cancellationToken);
            if (!IsSuccess(response.StatusCode))
            {
                throw new InvalidOperationException($"KRS bulletin for {day:yyyy-MM-dd} failed with status {(int)response.StatusCode}. Body: {Truncate(response.Content, 500)}");
            }

            var numbers = KrsBulletinParser.ParseKrsNumbers(response.Content)
                .Select(NormalizeKrsNumber)
                .Where(number => !string.IsNullOrWhiteSpace(number))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(number => number, StringComparer.Ordinal)
                .ToArray();

            logger.LogInformation("KRS bulletin {Day:yyyy-MM-dd} resolved {Count} unique numbers. Importing current excerpts now.", day, numbers.Length);

            if (numbers.Length == 0)
            {
                await SaveCheckpointAsync(checkpointKey, day, day, 0, 0, 0, 0, 0, completed: true, cancellationToken);
                continue;
            }

            var daySummary = await ProcessKrsNumbersAsync(importRunId, checkpointKey, day, day, numbers, remaining, cancellationToken);
            totalSummary += daySummary;
            if (!daySummary.Completed)
            {
                completed = false;
                break;
            }
        }

        return totalSummary with { Completed = completed };
    }

    private async Task<KrsImportSummary> ProcessKrsNumbersAsync(
        Guid importRunId,
        string checkpointKey,
        DateOnly checkpointFrom,
        DateOnly? checkpointTo,
        IReadOnlyList<string> krsNumbers,
        int maxItems,
        CancellationToken cancellationToken)
    {
        var checkpoint = options.Resume
            ? await store.GetCheckpointAsync(checkpointKey, cancellationToken)
            : null;

        var nextIndex = checkpoint?.Completed == true ? krsNumbers.Count : Math.Max(0, checkpoint?.NextItemIndex ?? 0);
        var checkpointImported = checkpoint?.ImportedCount ?? 0;
        var checkpointSkipped = checkpoint?.SkippedCount ?? 0;
        var checkpointFailed = checkpoint?.FailedCount ?? 0;
        var checkpointThrottled = 0L;

        logger.LogInformation(
            "KRS import batch started. Source={Source}, Register={Register}, Checkpoint={CheckpointKey}, Items={Items}, StartIndex={StartIndex}, MaxItems={MaxItems}, Resume={Resume}",
            options.Source,
            options.Register,
            checkpointKey,
            krsNumbers.Count,
            nextIndex,
            maxItems == int.MaxValue ? 0 : maxItems,
            options.Resume);

        var imported = 0L;
        var skipped = 0L;
        var failed = 0L;
        var throttled = 0L;
        var processedThisRun = 0;

        for (var index = nextIndex; index < krsNumbers.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (maxItems > 0 && processedThisRun >= maxItems)
            {
                break;
            }

            var result = await ImportSingleKrsAsync(krsNumbers[index], importRunId, cancellationToken);
            imported += result.Imported;
            skipped += result.Skipped;
            failed += result.Failed;
            throttled += result.Throttled;

            checkpointImported += result.Imported;
            checkpointSkipped += result.Skipped;
            checkpointFailed += result.Failed;
            checkpointThrottled += result.Throttled;
            processedThisRun++;

            await SaveCheckpointAsync(
                checkpointKey,
                checkpointFrom,
                checkpointTo,
                index + 1,
                checkpointImported,
                checkpointSkipped,
                checkpointFailed,
                checkpointThrottled,
                completed: index + 1 >= krsNumbers.Count,
                cancellationToken);
        }

        var completed = nextIndex + processedThisRun >= krsNumbers.Count;
        await SaveCheckpointAsync(
            checkpointKey,
            checkpointFrom,
            checkpointTo,
            Math.Min(nextIndex + processedThisRun, krsNumbers.Count),
            checkpointImported,
            checkpointSkipped,
            checkpointFailed,
            checkpointThrottled,
            completed,
            cancellationToken);

        return new KrsImportSummary(imported, skipped, failed, throttled, processedThisRun, krsNumbers.Count, completed);
    }

    private async Task<KrsItemResult> ImportSingleKrsAsync(string krsNumber, Guid importRunId, CancellationToken cancellationToken)
    {
        var throttled = 0L;
        var transientAttempts = 0;
        while (true)
        {
            try
            {
                var response = await client.GetCurrentExcerptAsync(krsNumber, options.Register, cancellationToken);
                if (IsTransient(response.StatusCode))
                {
                    throttled++;
                    transientAttempts++;
                    var delay = CalculateTransientDelay(response, transientAttempts);
                    logger.LogWarning(
                        "KRS {KrsNumber} returned transient status {Status}. Retrying same item in {DelaySeconds:n0}s. Body: {Body}",
                        krsNumber,
                        (int)response.StatusCode,
                        delay.TotalSeconds,
                        Truncate(response.Content, 500));
                    await Task.Delay(delay, cancellationToken);
                    continue;
                }

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    logger.LogWarning("KRS {KrsNumber} was not found in register {Register}.", krsNumber, options.Register);
                    return new KrsItemResult(0, 1, 0, throttled);
                }

                if (!IsSuccess(response.StatusCode))
                {
                    logger.LogWarning(
                        "KRS {KrsNumber} request failed with status {Status}. Body: {Body}",
                        krsNumber,
                        (int)response.StatusCode,
                        Truncate(response.Content, 500));
                    return new KrsItemResult(0, 0, 1, throttled);
                }

                if (string.IsNullOrWhiteSpace(response.Content))
                {
                    logger.LogWarning("KRS {KrsNumber} returned an empty response body.", krsNumber);
                    return new KrsItemResult(0, 0, 1, throttled);
                }

                var record = KrsCurrentExcerptParser.Parse(response);
                await store.UpsertKrsCompanyAsync(record, importRunId, cancellationToken);
                logger.LogInformation("Imported KRS {KrsNumber} ({Name}).", record.KrsNumber, record.Name);
                return new KrsItemResult(1, 0, 0, throttled);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "KRS {KrsNumber} import failed.", krsNumber);
                return new KrsItemResult(0, 0, 1, throttled);
            }
        }
    }

    private Task SaveCheckpointAsync(
        string checkpointKey,
        DateOnly checkpointFrom,
        DateOnly? checkpointTo,
        int nextIndex,
        long imported,
        long skipped,
        long failed,
        long throttled,
        bool completed,
        CancellationToken cancellationToken)
    {
        var checkpoint = new ImportCheckpoint(
            checkpointKey,
            "krs-current-excerpt",
            checkpointFrom,
            checkpointTo,
            1,
            nextIndex,
            imported,
            skipped,
            failed,
            completed,
            null);

        return store.SaveCheckpointAsync(checkpoint, new
        {
            options.Source,
            options.Register,
            options.StartDate,
            options.EndDate,
            checkpointFrom,
            checkpointTo,
            options.MaxItems,
            nextIndex,
            imported,
            skipped,
            failed,
            throttled,
            completed
        }, cancellationToken);
    }

    private string BuildSeededCheckpointKey(IReadOnlyList<string> krsNumbers) =>
        $"krs-seeded:{options.Register}:{Hash(string.Join('-', krsNumbers))}";

    private string BuildBulletinDayCheckpointKey(DateOnly day) =>
        $"krs-bulletin-day:{options.Register}:{day:yyyyMMdd}:{options.DayFormat}";

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string NormalizeKrsNumber(string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        return string.IsNullOrWhiteSpace(digits) ? string.Empty : digits.PadLeft(10, '0');
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.TooManyRequests
            or HttpStatusCode.RequestTimeout
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout
            or HttpStatusCode.BadGateway;

    private TimeSpan CalculateTransientDelay(CeidgRawResponse response, int attempt)
    {
        if (response.RetryAfter is { } retryAfter && retryAfter > TimeSpan.Zero)
        {
            return retryAfter <= TimeSpan.FromSeconds(options.TransientBackoffMaxSeconds)
                ? retryAfter
                : TimeSpan.FromSeconds(options.TransientBackoffMaxSeconds);
        }

        var baseSeconds = Math.Max(1, options.TransientBackoffBaseSeconds);
        var maxSeconds = Math.Max(baseSeconds, options.TransientBackoffMaxSeconds);
        var exponential = baseSeconds * Math.Pow(2, Math.Min(attempt - 1, 6));
        var jitter = Random.Shared.NextDouble() * baseSeconds;
        return TimeSpan.FromSeconds(Math.Min(maxSeconds, exponential + jitter));
    }

    private static bool IsSuccess(HttpStatusCode statusCode) =>
        (int)statusCode >= 200 && (int)statusCode <= 299;

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private sealed record KrsItemResult(long Imported, long Skipped, long Failed, long Throttled);

    private sealed record KrsImportSummary(long Imported, long Skipped, long Failed, long Throttled, long Processed, long Total, bool Completed)
    {
        public static KrsImportSummary Empty { get; } = new(0, 0, 0, 0, 0, 0, false);

        public static KrsImportSummary operator +(KrsImportSummary left, KrsImportSummary right) =>
            new(
                left.Imported + right.Imported,
                left.Skipped + right.Skipped,
                left.Failed + right.Failed,
                left.Throttled + right.Throttled,
                left.Processed + right.Processed,
                left.Total + right.Total,
                left.Completed && right.Completed);
    }
}
