using CeidgMirror.Application.Importing;
using CeidgMirror.Infrastructure.Ceidg;

namespace CeidgMirror.Worker;

public class Worker(
    ILogger<Worker> logger,
    ICeidgImportService importService,
    IKrsImportService krsImportService,
    ImportOptions importOptions,
    KrsImportOptions krsImportOptions,
    CeidgApiOptions ceidgApiOptions,
    IHostApplicationLifetime lifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "CEIDG mirror worker started. ImportEnabled={ImportEnabled}, KrsImportEnabled={KrsImportEnabled}, RunOnce={RunOnce}, Source={Source}, KrsSource={KrsSource}, StartPage={StartPage}, PageLimit={PageLimit}, MaxPages={MaxPages}, MaxCompanies={MaxCompanies}, Resume={Resume}, SkipExistingCompanies={SkipExistingCompanies}, HasJwtToken={HasJwtToken}, JwtTokenLength={JwtTokenLength}",
            importOptions.Enabled,
            krsImportOptions.Enabled,
            importOptions.RunOnce,
            importOptions.Source,
            krsImportOptions.Source,
            importOptions.StartPage,
            importOptions.PageLimit,
            importOptions.MaxPages,
            importOptions.MaxCompanies,
            importOptions.Resume,
            importOptions.SkipExistingCompanies,
            !string.IsNullOrWhiteSpace(ceidgApiOptions.JwtToken),
            ceidgApiOptions.JwtToken?.Length ?? 0);

        if (importOptions.RunOnce)
        {
            try
            {
                await RunEnabledImportsAsync(stoppingToken);
                logger.LogInformation("CEIDG mirror worker run-once flow finished.");
            }
            finally
            {
                lifetime.StopApplication();
            }

            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunEnabledImportsAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromMinutes(importOptions.LoopDelayMinutes), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "CEIDG mirror worker loop failed. It will retry from the saved checkpoint in {DelayMinutes} minutes.",
                    importOptions.FailureRetryDelayMinutes);
                await Task.Delay(TimeSpan.FromMinutes(importOptions.FailureRetryDelayMinutes), stoppingToken);
            }
        }
    }

    private async Task RunEnabledImportsAsync(CancellationToken stoppingToken)
    {
        if (importOptions.Enabled)
        {
            await importService.RunInitialImportAsync(stoppingToken);
        }

        if (krsImportOptions.Enabled)
        {
            await krsImportService.RunKrsImportAsync(stoppingToken);
        }

        if (!importOptions.Enabled && !krsImportOptions.Enabled)
        {
            logger.LogInformation("All import sources are disabled. Enable Import.Enabled or KrsImport.Enabled to start mirroring data.");
        }
    }
}
