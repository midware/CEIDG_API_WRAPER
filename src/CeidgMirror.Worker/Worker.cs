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

        if (!importOptions.Enabled && !krsImportOptions.Enabled)
        {
            logger.LogInformation("All import sources are disabled. Enable Import.Enabled or KrsImport.Enabled to start mirroring data.");
            return;
        }

        if (importOptions.RunOnce)
        {
            try
            {
                await Task.WhenAll(CreateRunOnceTasks(stoppingToken));
                logger.LogInformation("CEIDG mirror worker run-once flow finished.");
            }
            finally
            {
                lifetime.StopApplication();
            }

            return;
        }

        await Task.WhenAll(CreateContinuousImportTasks(stoppingToken));
    }

    private IEnumerable<Task> CreateRunOnceTasks(CancellationToken stoppingToken)
    {
        if (importOptions.Enabled)
        {
            yield return importService.RunInitialImportAsync(stoppingToken);
        }

        if (krsImportOptions.Enabled)
        {
            yield return krsImportService.RunKrsImportAsync(stoppingToken);
        }
    }

    private IEnumerable<Task> CreateContinuousImportTasks(CancellationToken stoppingToken)
    {
        if (importOptions.Enabled)
        {
            yield return RunImportLoopAsync(
                "CEIDG",
                () => importService.RunInitialImportAsync(stoppingToken),
                TimeSpan.FromMinutes(importOptions.LoopDelayMinutes),
                TimeSpan.FromMinutes(importOptions.FailureRetryDelayMinutes),
                stoppingToken);
        }

        if (krsImportOptions.Enabled)
        {
            yield return RunImportLoopAsync(
                "KRS",
                () => krsImportService.RunKrsImportAsync(stoppingToken),
                TimeSpan.FromMinutes(krsImportOptions.LoopDelayMinutes),
                TimeSpan.FromMinutes(krsImportOptions.FailureRetryDelayMinutes),
                stoppingToken);
        }
    }

    private async Task RunImportLoopAsync(
        string importName,
        Func<Task> runImportAsync,
        TimeSpan loopDelay,
        TimeSpan failureRetryDelay,
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await runImportAsync();
                logger.LogInformation("{ImportName} import loop finished one pass. Next pass in {DelayMinutes:n1} minutes.", importName, loopDelay.TotalMinutes);
                await Task.Delay(loopDelay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "{ImportName} import loop failed. It will retry from the saved checkpoint in {DelayMinutes:n1} minutes.",
                    importName,
                    failureRetryDelay.TotalMinutes);
                await Task.Delay(failureRetryDelay, stoppingToken);
            }
        }
    }
}
