using System.Net;
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

        var importRunId = await store.StartImportRunAsync("initial-index-detail", cancellationToken);
        var imported = 0;
        var failed = 0;
        var pagesRead = 0;
        var shouldStop = false;

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
                    throw new InvalidOperationException($"CEIDG /firmy failed with status {(int)indexResponse.StatusCode} on page {page}.");
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
                            logger.LogWarning(
                                "CEIDG detail failed with status {StatusCode} for CEIDG id {CeidgId}, NIP {Nip}, REGON {Regon}.",
                                (int)detailResponse.StatusCode,
                                item.CeidgId,
                                item.Nip,
                                item.Regon);
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
                new { imported, failed, pagesRead, options.StartPage, options.PageLimit, options.MaxPages, options.MaxCompanies },
                cancellationToken);

            logger.LogInformation("CEIDG import completed. Imported={Imported}, Failed={Failed}, PagesRead={PagesRead}.", imported, failed, pagesRead);
        }
        catch
        {
            await store.CompleteImportRunAsync(
                importRunId,
                "failed",
                new { imported, failed, pagesRead, options.StartPage, options.PageLimit, options.MaxPages, options.MaxCompanies },
                CancellationToken.None);
            throw;
        }
    }

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
