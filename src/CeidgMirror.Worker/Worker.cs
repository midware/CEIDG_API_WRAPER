using CeidgMirror.Application.Importing;

namespace CeidgMirror.Worker;

public class Worker(
    ILogger<Worker> logger,
    ICeidgImportService importService,
    ImportOptions importOptions,
    IHostApplicationLifetime lifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "CEIDG mirror worker started. ImportEnabled={ImportEnabled}, RunOnce={RunOnce}, StartPage={StartPage}, PageLimit={PageLimit}, MaxPages={MaxPages}, MaxCompanies={MaxCompanies}",
            importOptions.Enabled,
            importOptions.RunOnce,
            importOptions.StartPage,
            importOptions.PageLimit,
            importOptions.MaxPages,
            importOptions.MaxCompanies);

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
            await importService.RunInitialImportAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }
}
