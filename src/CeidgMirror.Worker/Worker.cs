using CeidgMirror.Infrastructure.Ceidg;

namespace CeidgMirror.Worker;

public class Worker(ILogger<Worker> logger, CeidgApiOptions options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "CEIDG mirror worker started. BaseUrl={BaseUrl}, HasJwtToken={HasJwtToken}, RequestLimit={RequestLimit}/{WindowSeconds}s",
            options.BaseUrl,
            !string.IsNullOrWhiteSpace(options.JwtToken),
            options.RequestLimit,
            options.WindowSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
