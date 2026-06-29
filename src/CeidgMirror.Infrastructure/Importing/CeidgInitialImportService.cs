using System.Globalization;
using System.Net;
using System.Text;
using CeidgMirror.Application.Abstractions;
using CeidgMirror.Application.Importing;
using CeidgMirror.Contracts;
using Microsoft.Extensions.Logging;

namespace CeidgMirror.Infrastructure.Importing;

public sealed class CeidgInitialImportService(
    ICeidgClient ceidgClient,
    ICompanyRecordStore store,
    ImportOptions options,
    ILogger<CeidgInitialImportService> logger) : ICeidgImportService
{
    public async Task RunInitialImportAsync(CancellationToken cancellationToken = default)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("CEIDG import is disabled. Set Import:Enabled=true to start importing.");
            return;
        }

        if (string.Equals(options.Source, "ReportRepository", StringComparison.OrdinalIgnoreCase))
        {
            await RunReportRepositoryImportAsync(cancellationToken);
            return;
        }

        if (string.Equals(options.Source, "RestApi", StringComparison.OrdinalIgnoreCase))
        {
            await RunRestIndexDetailImportAsync(cancellationToken);
            return;
        }

        await RunChangesImportAsync(cancellationToken);
    }

    private async Task RunChangesImportAsync(CancellationToken cancellationToken)
    {
        const string importKind = "changes-detail";
        var checkpointKey = BuildCheckpointKey(importKind);
        var checkpoint = options.Resume
            ? await store.GetCheckpointAsync(checkpointKey, cancellationToken)
            : null;

        if (checkpoint?.Completed == true && options.ChangesTo is not null)
        {
            logger.LogInformation(
                "CEIDG changes import checkpoint {CheckpointKey} is already completed at page {Page}, item {ItemIndex}.",
                checkpoint.CheckpointKey,
                checkpoint.NextPage,
                checkpoint.NextItemIndex);
            return;
        }

        var importRunId = await store.StartImportRunAsync(importKind, cancellationToken);
        var importedThisRun = 0;
        var failedThisRun = 0;
        var skippedThisRun = 0;
        var pagesReadThisRun = 0;
        var discoveredThisRun = 0;
        var importedTotal = checkpoint?.ImportedCount ?? 0;
        var skippedTotal = checkpoint?.SkippedCount ?? 0;
        var failedTotal = checkpoint?.FailedCount ?? 0;
        var windowFrom = checkpoint?.ChangesFrom ?? options.ChangesFrom;
        DateOnly? windowTo = checkpoint?.ChangesTo ?? CalculateWindowTo(windowFrom);
        var startPage = checkpoint?.NextPage ?? options.StartPage;
        var startItemIndex = checkpoint?.NextItemIndex ?? 0;
        var shouldStop = false;
        var completed = false;
        string? lastFailure = null;
        string? lastCompanyId = checkpoint?.LastCompanyId;

        try
        {
            while (!shouldStop)
            {
                if (windowFrom > EffectiveFinalDate())
                {
                    completed = options.ChangesTo is not null;
                    await SaveChangesCheckpointAsync(
                        checkpointKey,
                        importKind,
                        windowFrom,
                        windowTo,
                        1,
                        0,
                        importedTotal,
                        skippedTotal,
                        failedTotal,
                        completed,
                        lastCompanyId,
                        cancellationToken);
                    logger.LogInformation("CEIDG changes import reached the current final date at {WindowFrom}.", windowFrom);
                    break;
                }

                for (var page = startPage; !shouldStop; page++)
                {
                    if (options.MaxPages > 0 && pagesReadThisRun >= options.MaxPages)
                    {
                        logger.LogInformation("CEIDG changes import paused after configured MaxPages={MaxPages}.", options.MaxPages);
                        shouldStop = true;
                        break;
                    }

                    logger.LogInformation(
                        "Requesting CEIDG /zmiana for {WindowFrom}..{WindowTo} page {Page} limit {Limit}.",
                        windowFrom,
                        windowTo,
                        page,
                        options.PageLimit);

                    var changesResponse = await ceidgClient.GetChangesAsync(
                        windowFrom,
                        windowTo,
                        page,
                        options.PageLimit,
                        cancellationToken);

                    pagesReadThisRun++;

                    if (changesResponse.StatusCode == HttpStatusCode.NoContent)
                    {
                        logger.LogInformation("CEIDG /zmiana returned 204 for {WindowFrom}..{WindowTo} page {Page}.", windowFrom, windowTo, page);
                        break;
                    }

                    if (!changesResponse.IsSuccess)
                    {
                        lastFailure = $"CEIDG /zmiana failed with status {(int)changesResponse.StatusCode} for {windowFrom}..{windowTo} page {page}. Body: {Truncate(changesResponse.Content, 500)}";
                        logger.LogError("{Failure}", lastFailure);
                        throw new InvalidOperationException(lastFailure);
                    }

                    EnsureJsonResponse(changesResponse, "CEIDG /zmiana");
                    var changesPage = CeidgZmianaResponseParser.ParsePage(changesResponse.Content);
                    var companyIds = changesPage.CompanyIds;
                    discoveredThisRun += companyIds.Count;

                    if (companyIds.Count == 0)
                    {
                        logger.LogInformation("CEIDG /zmiana returned no ids for {WindowFrom}..{WindowTo} page {Page}.", windowFrom, windowTo, page);
                        break;
                    }

                    var itemStart = page == startPage ? startItemIndex : 0;
                    logger.LogInformation(
                        "CEIDG /zmiana {WindowFrom}..{WindowTo} page {Page} returned {Count} ids. Starting at item index {ItemIndex}. Total count={TotalCount}.",
                        windowFrom,
                        windowTo,
                        page,
                        companyIds.Count,
                        itemStart,
                        changesPage.Count);

                    for (var itemIndex = itemStart; itemIndex < companyIds.Count; itemIndex++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (options.MaxCompanies > 0 && importedThisRun >= options.MaxCompanies)
                        {
                            shouldStop = true;
                            logger.LogInformation("CEIDG changes import paused after configured MaxCompanies={MaxCompanies}.", options.MaxCompanies);
                            break;
                        }

                        var companyId = companyIds[itemIndex];
                        lastCompanyId = companyId;

                        try
                        {
                            if (options.SkipExistingCompanies && await store.CompanyExistsAsync(companyId, cancellationToken))
                            {
                                skippedThisRun++;
                                skippedTotal++;
                                logger.LogInformation(
                                    "Skipping existing CEIDG company {CompanyId} from {WindowFrom}..{WindowTo} page {Page} item {ItemIndex}/{ItemCount}. SkippedTotal={SkippedTotal}.",
                                    companyId,
                                    windowFrom,
                                    windowTo,
                                    page,
                                    itemIndex + 1,
                                    companyIds.Count,
                                    skippedTotal);
                                await SaveChangesCheckpointAsync(
                                    checkpointKey,
                                    importKind,
                                    windowFrom,
                                    windowTo,
                                    page,
                                    itemIndex + 1,
                                    importedTotal,
                                    skippedTotal,
                                    failedTotal,
                                    completed: false,
                                    lastCompanyId,
                                    cancellationToken);
                                continue;
                            }

                            logger.LogInformation(
                                "Requesting CEIDG /firma/{CompanyId} from {WindowFrom}..{WindowTo} page {Page} item {ItemIndex}/{ItemCount}.",
                                companyId,
                                windowFrom,
                                windowTo,
                                page,
                                itemIndex + 1,
                                companyIds.Count);

                            var detailResponse = await ceidgClient.GetCompanyByIdAsync(companyId, cancellationToken);
                            if (detailResponse.StatusCode == HttpStatusCode.NoContent)
                            {
                                failedThisRun++;
                                failedTotal++;
                                lastFailure = $"CEIDG /firma/{companyId} returned 204.";
                                logger.LogWarning("{Failure}", lastFailure);
                            }
                            else if (!detailResponse.IsSuccess)
                            {
                                failedThisRun++;
                                failedTotal++;
                                lastFailure = $"CEIDG detail failed with status {(int)detailResponse.StatusCode} for id {companyId}. Body: {Truncate(detailResponse.Content, 500)}";
                                logger.LogWarning("{Failure}", lastFailure);
                            }
                            else
                            {
                                EnsureJsonResponse(detailResponse, $"CEIDG /firma/{companyId}");
                                await store.UpsertCompanyAsync(new CompanyIndexItem(companyId, null, null, null, "{}"), detailResponse, importRunId, cancellationToken);
                                importedThisRun++;
                                importedTotal++;
                                logger.LogInformation(
                                    "Imported CEIDG company {CompanyId}. ImportedThisRun={ImportedThisRun}, ImportedTotal={ImportedTotal}.",
                                    companyId,
                                    importedThisRun,
                                    importedTotal);
                            }
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            failedThisRun++;
                            failedTotal++;
                            lastFailure = ex.Message;
                            logger.LogError(ex, "Failed to import CEIDG company id {CompanyId}.", companyId);
                        }

                        await SaveChangesCheckpointAsync(
                            checkpointKey,
                            importKind,
                            windowFrom,
                            windowTo,
                            page,
                            itemIndex + 1,
                            importedTotal,
                            skippedTotal,
                            failedTotal,
                            completed: false,
                            lastCompanyId,
                            cancellationToken);
                    }

                    if (shouldStop)
                    {
                        break;
                    }

                    if (companyIds.Count < options.PageLimit)
                    {
                        break;
                    }

                    await SaveChangesCheckpointAsync(
                        checkpointKey,
                        importKind,
                        windowFrom,
                        windowTo,
                        page + 1,
                        0,
                        importedTotal,
                        skippedTotal,
                        failedTotal,
                        completed: false,
                        lastCompanyId,
                        cancellationToken);
                }

                if (shouldStop)
                {
                    break;
                }

                (windowFrom, windowTo) = NextWindow(windowTo);
                startPage = 1;
                startItemIndex = 0;

                await SaveChangesCheckpointAsync(
                    checkpointKey,
                    importKind,
                    windowFrom,
                    windowTo,
                    startPage,
                    startItemIndex,
                    importedTotal,
                    skippedTotal,
                    failedTotal,
                    completed: false,
                    lastCompanyId,
                    cancellationToken);
            }

            await store.CompleteImportRunAsync(
                importRunId,
                completed ? "completed" : "paused",
                new
                {
                    importedThisRun,
                    skippedThisRun,
                    failedThisRun,
                    pagesReadThisRun,
                    discoveredThisRun,
                    importedTotal,
                    skippedTotal,
                    failedTotal,
                    completed,
                    lastFailure,
                    lastCompanyId,
                    checkpointKey,
                    windowFrom,
                    windowTo,
                    options.ChangesFrom,
                    options.ChangesTo,
                    options.ChangesWindowDays,
                    options.StartPage,
                    options.PageLimit,
                    options.MaxPages,
                    options.MaxCompanies,
                    options.SkipExistingCompanies
                },
                cancellationToken);

            logger.LogInformation(
                "CEIDG changes import {Status}. ImportedThisRun={Imported}, SkippedThisRun={Skipped}, FailedThisRun={Failed}, ImportedTotal={ImportedTotal}, SkippedTotal={SkippedTotal}, FailedTotal={FailedTotal}.",
                completed ? "completed" : "paused",
                importedThisRun,
                skippedThisRun,
                failedThisRun,
                importedTotal,
                skippedTotal,
                failedTotal);
        }
        catch
        {
            await store.CompleteImportRunAsync(
                importRunId,
                "failed",
                new
                {
                    importedThisRun,
                    skippedThisRun,
                    failedThisRun,
                    pagesReadThisRun,
                    discoveredThisRun,
                    importedTotal,
                    skippedTotal,
                    failedTotal,
                    completed,
                    lastFailure,
                    lastCompanyId,
                    checkpointKey,
                    windowFrom,
                    windowTo,
                    options.ChangesFrom,
                    options.ChangesTo,
                    options.ChangesWindowDays,
                    options.StartPage,
                    options.PageLimit,
                    options.MaxPages,
                    options.MaxCompanies,
                    options.SkipExistingCompanies
                },
                CancellationToken.None);
            throw;
        }
    }
    private async Task RunReportRepositoryImportAsync(CancellationToken cancellationToken)
    {
        var importRunId = await store.StartImportRunAsync("report-repository", cancellationToken);
        var downloaded = 0;
        var failed = 0;
        var catalogCount = 0;
        string? lastFailure = null;

        try
        {
            var catalogResponse = await ceidgClient.GetReportRepositoryCatalogAsync(cancellationToken);
            if (!catalogResponse.IsSuccess)
            {
                lastFailure = $"CEIDG report catalog failed with status {(int)catalogResponse.StatusCode}. Body: {Truncate(catalogResponse.Content, 500)}";
                logger.LogError("{Failure}", lastFailure);
                throw new InvalidOperationException(lastFailure);
            }

            var reports = CeidgReportRepositoryParser.ParseReports(catalogResponse.Content);
            catalogCount = reports.Count;
            var selectedReports = reports
                .Where(MatchesReportFilter)
                .OrderByDescending(report => report.GeneratedOn ?? DateTimeOffset.MinValue)
                .Take(options.MaxReports > 0 ? options.MaxReports : int.MaxValue)
                .ToArray();

            logger.LogInformation(
                "CEIDG report catalog returned {CatalogCount} reports. Selected {SelectedCount} reports for download.",
                catalogCount,
                selectedReports.Length);

            foreach (var report in selectedReports)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var payloadResponse = await ceidgClient.DownloadReportFromRepositoryAsync(report.GeneratedReportId, cancellationToken: cancellationToken);
                    if (!payloadResponse.IsSuccess)
                    {
                        failed++;
                        lastFailure = $"CEIDG report download failed with status {(int)payloadResponse.StatusCode} for generatedReportId {report.GeneratedReportId}. Body: {Truncate(payloadResponse.Content, 500)}";
                        logger.LogWarning("{Failure}", lastFailure);
                        continue;
                    }

                    await store.UpsertReportPayloadAsync(report, payloadResponse, importRunId, cancellationToken);
                    downloaded++;
                    logger.LogInformation(
                        "Downloaded CEIDG report {ReportName} ({FileType}) generated at {GeneratedOn}, bytes/chars={Length}.",
                        report.ReportName,
                        report.FileType,
                        report.GeneratedOn,
                        payloadResponse.Content.Length);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failed++;
                    lastFailure = ex.Message;
                    logger.LogError(ex, "Failed to download CEIDG report {GeneratedReportId}.", report.GeneratedReportId);
                }
            }

            await store.CompleteImportRunAsync(
                importRunId,
                "completed",
                new { downloaded, failed, catalogCount, lastFailure, options.MaxReports, options.ReportNameContains, options.ReportFileType },
                cancellationToken);

            logger.LogInformation("CEIDG report import completed. Downloaded={Downloaded}, Failed={Failed}, CatalogCount={CatalogCount}.", downloaded, failed, catalogCount);
        }
        catch
        {
            await store.CompleteImportRunAsync(
                importRunId,
                "failed",
                new { downloaded, failed, catalogCount, lastFailure, options.MaxReports, options.ReportNameContains, options.ReportFileType },
                CancellationToken.None);
            throw;
        }
    }

    private async Task RunRestIndexDetailImportAsync(CancellationToken cancellationToken)
    {
        var importRunId = await store.StartImportRunAsync("initial-index-detail", cancellationToken);
        var imported = 0;
        var failed = 0;
        var pagesRead = 0;
        var shouldStop = false;
        string? lastFailure = null;

        try
        {
            for (var page = options.StartPage; !shouldStop; page++)
            {
                if (options.MaxPages > 0 && pagesRead >= options.MaxPages)
                {
                    break;
                }

                var indexResponse = await ceidgClient.GetCompaniesAsync(
                    new CeidgFirmySearchRequest
                    {
                        Page = page,
                        Limit = options.PageLimit
                    },
                    cancellationToken);

                pagesRead++;

                if (indexResponse.StatusCode == HttpStatusCode.NoContent)
                {
                    logger.LogInformation("CEIDG /firmy returned 204 on page {Page}. Import page loop ended.", page);
                    break;
                }

                if (!indexResponse.IsSuccess)
                {
                    lastFailure = $"CEIDG /firmy failed with status {(int)indexResponse.StatusCode} on page {page}. Body: {Truncate(indexResponse.Content, 500)}";
                    logger.LogError("{Failure}", lastFailure);
                    throw new InvalidOperationException(lastFailure);
                }

                var indexItems = CeidgFirmyResponseParser.ParseCompanies(indexResponse.Content);
                if (indexItems.Count == 0)
                {
                    logger.LogInformation("CEIDG /firmy returned no items on page {Page}. Import page loop ended.", page);
                    break;
                }

                logger.LogInformation("CEIDG /firmy page {Page} returned {Count} companies.", page, indexItems.Count);

                foreach (var item in indexItems)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (options.MaxCompanies > 0 && imported >= options.MaxCompanies)
                    {
                        shouldStop = true;
                        break;
                    }

                    try
                    {
                        var detailResponse = await GetDetailAsync(item, cancellationToken);
                        if (detailResponse.StatusCode == HttpStatusCode.NoContent)
                        {
                            logger.LogWarning("CEIDG detail returned 204 for CEIDG id {CeidgId}, NIP {Nip}, REGON {Regon}.", item.CeidgId, item.Nip, item.Regon);
                            continue;
                        }

                        if (!detailResponse.IsSuccess)
                        {
                            failed++;
                            lastFailure = $"CEIDG detail failed with status {(int)detailResponse.StatusCode} for CEIDG id {item.CeidgId}, NIP {item.Nip}, REGON {item.Regon}. Body: {Truncate(detailResponse.Content, 500)}";
                            logger.LogWarning("{Failure}", lastFailure);
                            continue;
                        }

                        await store.UpsertCompanyAsync(item, detailResponse, importRunId, cancellationToken);
                        imported++;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        failed++;
                        logger.LogError(ex, "Failed to import CEIDG id {CeidgId}, NIP {Nip}, REGON {Regon}.", item.CeidgId, item.Nip, item.Regon);
                    }
                }
            }

            await store.CompleteImportRunAsync(
                importRunId,
                "completed",
                new { imported, failed, pagesRead, lastFailure, options.StartPage, options.PageLimit, options.MaxPages, options.MaxCompanies },
                cancellationToken);

            logger.LogInformation("CEIDG import completed. Imported={Imported}, Failed={Failed}, PagesRead={PagesRead}.", imported, failed, pagesRead);
        }
        catch
        {
            await store.CompleteImportRunAsync(
                importRunId,
                "failed",
                new { imported, failed, pagesRead, lastFailure, options.StartPage, options.PageLimit, options.MaxPages, options.MaxCompanies },
                CancellationToken.None);
            throw;
        }
    }


    private Task SaveChangesCheckpointAsync(
        string checkpointKey,
        string importKind,
        DateOnly windowFrom,
        DateOnly? windowTo,
        int nextPage,
        int nextItemIndex,
        long importedTotal,
        long skippedTotal,
        long failedTotal,
        bool completed,
        string? lastCompanyId,
        CancellationToken cancellationToken)
    {
        var checkpoint = new ImportCheckpoint(
            checkpointKey,
            importKind,
            windowFrom,
            windowTo,
            nextPage,
            nextItemIndex,
            importedTotal,
            skippedTotal,
            failedTotal,
            completed,
            lastCompanyId);

        return store.SaveCheckpointAsync(
            checkpoint,
            new
            {
                options.Source,
                options.PageLimit,
                options.MaxPages,
                options.MaxCompanies,
                options.ChangesWindowDays,
                options.SkipExistingCompanies,
                options.Resume
            },
            cancellationToken);
    }

    private string BuildCheckpointKey(string importKind)
    {
        var to = options.ChangesTo?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? "open";
        return $"{importKind}:{options.ChangesFrom:yyyyMMdd}:{to}:window-{Math.Max(1, options.ChangesWindowDays)}:limit-{options.PageLimit}";
    }

    private DateOnly CalculateWindowTo(DateOnly windowFrom)
    {
        var windowDays = Math.Max(1, options.ChangesWindowDays);
        var proposed = windowFrom.AddDays(windowDays - 1);
        var finalDate = EffectiveFinalDate();
        return proposed <= finalDate ? proposed : finalDate;
    }

    private (DateOnly WindowFrom, DateOnly? WindowTo) NextWindow(DateOnly? currentWindowTo)
    {
        var nextFrom = (currentWindowTo ?? EffectiveFinalDate()).AddDays(1);
        return (nextFrom, CalculateWindowTo(nextFrom));
    }

    private DateOnly EffectiveFinalDate() => options.ChangesTo ?? DateOnly.FromDateTime(DateTime.UtcNow);

    private static void EnsureJsonResponse(CeidgRawResponse response, string operationName)
    {
        var trimmed = response.Content.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] is '{' or '[')
        {
            return;
        }

        throw new InvalidOperationException(
            $"{operationName} returned non-JSON content. Status={(int)response.StatusCode}, ContentType={response.ContentType ?? "<none>"}, Uri={response.RequestUri}, BodyStart={Truncate(response.Content, 500)}");
    }

    private bool MatchesReportFilter(CeidgReportDescriptor report)
    {
        if (!string.IsNullOrWhiteSpace(options.ReportFileType) &&
            !string.Equals(report.FileType, options.ReportFileType, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(options.ReportNameContains))
        {
            return true;
        }

        return CultureInfo.InvariantCulture.CompareInfo.IndexOf(
            RemoveDiacritics(report.ReportName),
            RemoveDiacritics(options.ReportNameContains),
            CompareOptions.IgnoreCase) >= 0;
    }

    private static string RemoveDiacritics(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (character == '\u0142')
            {
                builder.Append('l');
                continue;
            }

            if (character == '\u0141')
            {
                builder.Append('L');
                continue;
            }

            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private Task<CeidgRawResponse> GetDetailAsync(CompanyIndexItem item, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(item.Nip))
        {
            return ceidgClient.GetCompanyDetailsAsync(new CeidgCompanyDetailRequest { Nip = item.Nip }, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(item.Regon))
        {
            return ceidgClient.GetCompanyDetailsAsync(new CeidgCompanyDetailRequest { Regon = item.Regon }, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(item.CeidgId))
        {
            return ceidgClient.GetCompanyByIdAsync(item.CeidgId, cancellationToken);
        }

        throw new InvalidOperationException("CEIDG index item does not contain NIP, REGON or CEIDG id.");
    }
}
