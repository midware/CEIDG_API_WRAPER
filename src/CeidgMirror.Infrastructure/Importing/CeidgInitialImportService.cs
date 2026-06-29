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
        var importRunId = await store.StartImportRunAsync("changes-detail", cancellationToken);
        var imported = 0;
        var failed = 0;
        var pagesRead = 0;
        var discovered = 0;
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

                var changesResponse = await ceidgClient.GetChangesAsync(
                    options.ChangesFrom,
                    options.ChangesTo,
                    page,
                    options.PageLimit,
                    cancellationToken);

                pagesRead++;

                if (changesResponse.StatusCode == HttpStatusCode.NoContent)
                {
                    logger.LogInformation("CEIDG /zmiana returned 204 on page {Page}. Import page loop ended.", page);
                    break;
                }

                if (!changesResponse.IsSuccess)
                {
                    lastFailure = $"CEIDG /zmiana failed with status {(int)changesResponse.StatusCode} on page {page}. Body: {Truncate(changesResponse.Content, 500)}";
                    logger.LogError("{Failure}", lastFailure);
                    throw new InvalidOperationException(lastFailure);
                }

                var companyIds = CeidgZmianaResponseParser.ParseCompanyIds(changesResponse.Content);
                discovered += companyIds.Count;
                if (companyIds.Count == 0)
                {
                    logger.LogInformation("CEIDG /zmiana returned no ids on page {Page}. Import page loop ended.", page);
                    break;
                }

                logger.LogInformation("CEIDG /zmiana page {Page} returned {Count} company ids.", page, companyIds.Count);

                foreach (var companyId in companyIds)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (options.MaxCompanies > 0 && imported >= options.MaxCompanies)
                    {
                        shouldStop = true;
                        break;
                    }

                    try
                    {
                        var detailResponse = await ceidgClient.GetCompanyByIdAsync(companyId, cancellationToken);
                        if (detailResponse.StatusCode == HttpStatusCode.NoContent)
                        {
                            logger.LogWarning("CEIDG /firma/{CompanyId} returned 204.", companyId);
                            continue;
                        }

                        if (!detailResponse.IsSuccess)
                        {
                            failed++;
                            lastFailure = $"CEIDG detail failed with status {(int)detailResponse.StatusCode} for id {companyId}. Body: {Truncate(detailResponse.Content, 500)}";
                            logger.LogWarning("{Failure}", lastFailure);
                            continue;
                        }

                        await store.UpsertCompanyAsync(new CompanyIndexItem(companyId, null, null, null, "{}"), detailResponse, importRunId, cancellationToken);
                        imported++;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        failed++;
                        lastFailure = ex.Message;
                        logger.LogError(ex, "Failed to import CEIDG company id {CompanyId}.", companyId);
                    }
                }
            }

            await store.CompleteImportRunAsync(
                importRunId,
                "completed",
                new { imported, failed, discovered, pagesRead, lastFailure, options.ChangesFrom, options.ChangesTo, options.StartPage, options.PageLimit, options.MaxPages, options.MaxCompanies },
                cancellationToken);

            logger.LogInformation("CEIDG changes import completed. Imported={Imported}, Failed={Failed}, Discovered={Discovered}, PagesRead={PagesRead}.", imported, failed, discovered, pagesRead);
        }
        catch
        {
            await store.CompleteImportRunAsync(
                importRunId,
                "failed",
                new { imported, failed, discovered, pagesRead, lastFailure, options.ChangesFrom, options.ChangesTo, options.StartPage, options.PageLimit, options.MaxPages, options.MaxCompanies },
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
            if (character == 'ł')
            {
                builder.Append('l');
                continue;
            }

            if (character == 'Ł')
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
