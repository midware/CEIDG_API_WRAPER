using CeidgMirror.Application.Importing;
using CeidgMirror.Infrastructure.Ceidg;

namespace CeidgMirror.Infrastructure.Krs;

public sealed class KrsRequestPacer
{
    private readonly SlidingWindowRequestPacer pacer;

    public KrsRequestPacer(KrsImportOptions options)
    {
        pacer = new SlidingWindowRequestPacer(
            TimeSpan.FromSeconds(options.MinimumRequestIntervalSeconds),
            new SlidingWindowRequestPacer.Window(options.RequestLimit, TimeSpan.FromSeconds(options.WindowSeconds)));
    }

    public Task WaitForSlotAsync(CancellationToken cancellationToken) => pacer.WaitForSlotAsync(cancellationToken);
}
