using CeidgMirror.Application.Importing;
using CeidgMirror.Infrastructure.Ceidg;

namespace CeidgMirror.Worker;

public class Worker(
    ILogger<Worker> logger,
    ICeidgImportService importService,
    ImportOptions importOptions,
    CeidgApiOptions ceidgApiOptions,
    IHostApplicationLifetime lifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "CEIDG mirror worker started. ImportEnabled={ImportEnabled}, RunOnce={RunOnce}, Source={Source}, StartPage={StartPage}, PageLimit={PageLimit}, MaxPages={MaxPages}, MaxCompanies={MaxCompanies}, Resume={Resume}, SkipExistingCompanies={SkipExistingCompanies}, HasJwtToken={HasJwtToken}, JwtTokenLength={JwtTokenLength}",
            importOptions.Enabled,
            importOptions.RunOnce,
            importOptions.Source,
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
                await importService.RunInitialImportAsync(stoppingToken);
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
                await importService.RunInitialImportAsync(stoppingToken);
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
}
