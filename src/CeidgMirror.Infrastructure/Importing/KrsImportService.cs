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
        var imported = 0L;
        var skipped = 0L;
        var failed = 0L;
        var throttled = 0L;

        try
        {
            var krsNumbers = await ResolveKrsNumbersAsync(cancellationToken);
            var checkpointKey = BuildCheckpointKey(krsNumbers);
            var checkpoint = options.Resume
                ? await store.GetCheckpointAsync(checkpointKey, cancellationToken)
                : null;

            var nextIndex = checkpoint?.Completed == true ? krsNumbers.Count : Math.Max(0, checkpoint?.NextItemIndex ?? 0);
            imported = checkpoint?.ImportedCount ?? 0;
            skipped = checkpoint?.SkippedCount ?? 0;
            failed = checkpoint?.FailedCount ?? 0;

            logger.LogInformation(
                "KRS import started. Source={Source}, Register={Register}, Items={Items}, StartIndex={StartIndex}, MaxItems={MaxItems}, Resume={Resume}",
                options.Source,
                options.Register,
                krsNumbers.Count,
                nextIndex,
                options.MaxItems,
                options.Resume);

            var processedThisRun = 0;
            for (var index = nextIndex; index < krsNumbers.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (options.MaxItems > 0 && processedThisRun >= options.MaxItems)
                {
                    break;
                }

                var krsNumber = krsNumbers[index];
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
                            skipped++;
                            logger.LogWarning("KRS {KrsNumber} was not found in register {Register}.", krsNumber, options.Register);
                        }
                        else if (!IsSuccess(response.StatusCode))
                        {
                            failed++;
                            logger.LogWarning(
                                "KRS {KrsNumber} request failed with status {Status}. Body: {Body}",
                                krsNumber,
                                (int)response.StatusCode,
                                Truncate(response.Content, 500));
                        }
                        else if (string.IsNullOrWhiteSpace(response.Content))
                        {
                            failed++;
                            logger.LogWarning("KRS {KrsNumber} returned an empty response body.", krsNumber);
                        }
                        else
                        {
                            var record = KrsCurrentExcerptParser.Parse(response);
                            await store.UpsertKrsCompanyAsync(record, importRunId, cancellationToken);
                            imported++;
                            logger.LogInformation("Imported KRS {KrsNumber} ({Name}).", record.KrsNumber, record.Name);
                        }

                        break;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        failed++;
                        logger.LogError(ex, "KRS {KrsNumber} import failed.", krsNumber);
                        break;
                    }
                }

                processedThisRun++;
                await SaveCheckpointAsync(checkpointKey, index + 1, imported, skipped, failed, throttled, completed: index + 1 >= krsNumbers.Count, cancellationToken);
            }

            var completed = nextIndex + processedThisRun >= krsNumbers.Count;
            await SaveCheckpointAsync(checkpointKey, Math.Min(nextIndex + processedThisRun, krsNumbers.Count), imported, skipped, failed, throttled, completed, cancellationToken);
            await store.CompleteImportRunAsync(importRunId, completed ? "completed" : "partial", new
            {
                options.Source,
                options.Register,
                options.MaxItems,
                imported,
                skipped,
                failed,
                throttled,
                total = krsNumbers.Count,
                completed
            }, cancellationToken);
        }
        catch
        {
            await store.CompleteImportRunAsync(importRunId, "failed", new
            {
                options.Source,
                options.Register,
                imported,
                skipped,
                failed,
                throttled
            }, cancellationToken);
            throw;
        }
    }

    private async Task<IReadOnlyList<string>> ResolveKrsNumbersAsync(CancellationToken cancellationToken)
    {
        if (string.Equals(options.Source, "SeededNumbers", StringComparison.OrdinalIgnoreCase))
        {
            var seeded = options.SeedKrsNumbers
                .Select(NormalizeKrsNumber)
                .Where(number => !string.IsNullOrWhiteSpace(number))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(number => number, StringComparer.Ordinal)
                .ToArray();

            logger.LogInformation("KRS seeded source resolved {Count} numbers.", seeded.Length);
            return seeded;
        }

        if (string.Equals(options.Source, "Bulletin", StringComparison.OrdinalIgnoreCase))
        {
            var endDate = options.EndDate ?? DateOnly.FromDateTime(DateTime.UtcNow.Date);
            var numbers = new SortedSet<string>(StringComparer.Ordinal);
            logger.LogInformation("Resolving KRS numbers from bulletin window {StartDate:yyyy-MM-dd}..{EndDate:yyyy-MM-dd}.", options.StartDate, endDate);
            for (var day = options.StartDate; day <= endDate; day = day.AddDays(1))
            {
                logger.LogInformation("Requesting KRS bulletin for {Day:yyyy-MM-dd}.", day);
                var response = await client.GetBulletinAsync(day, options.DayFormat, cancellationToken);
                if (!IsSuccess(response.StatusCode))
                {
                    throw new InvalidOperationException($"KRS bulletin for {day:yyyy-MM-dd} failed with status {(int)response.StatusCode}. Body: {Truncate(response.Content, 500)}");
                }

                var before = numbers.Count;
                foreach (var number in KrsBulletinParser.ParseKrsNumbers(response.Content))
                {
                    numbers.Add(number);
                }

                logger.LogInformation("KRS bulletin {Day:yyyy-MM-dd} resolved {NewCount} new numbers. Total unique={TotalCount}.", day, numbers.Count - before, numbers.Count);
            }

            logger.LogInformation("KRS bulletin source resolved {Count} unique numbers.", numbers.Count);
            return numbers.ToArray();
        }

        throw new InvalidOperationException($"Unsupported KRS import source: {options.Source}. Use SeededNumbers or Bulletin.");
    }

    private Task SaveCheckpointAsync(
        string checkpointKey,
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
            options.StartDate,
            options.EndDate,
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
            options.MaxItems,
            nextIndex,
            imported,
            skipped,
            failed,
            throttled,
            completed
        }, cancellationToken);
    }

    private string BuildCheckpointKey(IReadOnlyList<string> krsNumbers) =>
        string.Equals(options.Source, "SeededNumbers", StringComparison.OrdinalIgnoreCase)
            ? $"krs-seeded:{options.Register}:{Hash(string.Join('-', krsNumbers))}"
            : $"krs-bulletin:{options.Register}:{options.StartDate:yyyyMMdd}:{options.EndDate?.ToString("yyyyMMdd") ?? "open"}:{options.DayFormat}";

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
}
