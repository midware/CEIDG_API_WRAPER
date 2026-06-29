namespace CeidgMirror.Infrastructure.Ceidg;

public sealed class SlidingWindowRequestPacer(TimeSpan minimumInterval, params SlidingWindowRequestPacer.Window[] windows)
{
    private readonly WindowState[] _windows = windows.Select(window => new WindowState(window)).ToArray();
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private DateTimeOffset? _lastRequestAt;

    public async Task WaitForSlotAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            TimeSpan waitFor = TimeSpan.Zero;

            await _mutex.WaitAsync(cancellationToken);
            try
            {
                var now = DateTimeOffset.UtcNow;

                if (_lastRequestAt is not null && minimumInterval > TimeSpan.Zero)
                {
                    waitFor = Max(waitFor, minimumInterval - (now - _lastRequestAt.Value));
                }

                foreach (var window in _windows)
                {
                    window.Prune(now);
                    if (window.IsFull)
                    {
                        waitFor = Max(waitFor, window.TimeUntilSlot(now));
                    }
                }

                if (waitFor <= TimeSpan.Zero)
                {
                    foreach (var window in _windows)
                    {
                        window.Register(now);
                    }

                    _lastRequestAt = now;
                    return;
                }
            }
            finally
            {
                _mutex.Release();
            }

            await Task.Delay(waitFor, cancellationToken);
        }
    }

    private static TimeSpan Max(TimeSpan left, TimeSpan right) => left >= right ? left : right;

    public sealed record Window(int RequestLimit, TimeSpan Duration);

    private sealed class WindowState(Window window)
    {
        private readonly Queue<DateTimeOffset> _requests = new();

        public bool IsFull => _requests.Count >= window.RequestLimit;

        public void Register(DateTimeOffset timestamp) => _requests.Enqueue(timestamp);

        public void Prune(DateTimeOffset now)
        {
            while (_requests.Count > 0 && now - _requests.Peek() >= window.Duration)
            {
                _requests.Dequeue();
            }
        }

        public TimeSpan TimeUntilSlot(DateTimeOffset now)
        {
            if (_requests.Count == 0)
            {
                return TimeSpan.Zero;
            }

            var waitFor = window.Duration - (now - _requests.Peek());
            return waitFor > TimeSpan.Zero ? waitFor : TimeSpan.Zero;
        }
    }
}