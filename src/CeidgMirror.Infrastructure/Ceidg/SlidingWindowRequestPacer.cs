namespace CeidgMirror.Infrastructure.Ceidg;

public sealed class SlidingWindowRequestPacer(int requestLimit, TimeSpan window)
{
    private readonly Queue<DateTimeOffset> _requests = new();
    private readonly SemaphoreSlim _mutex = new(1, 1);

    public async Task WaitForSlotAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            TimeSpan? waitFor = null;

            await _mutex.WaitAsync(cancellationToken);
            try
            {
                var now = DateTimeOffset.UtcNow;

                while (_requests.Count > 0 && now - _requests.Peek() >= window)
                {
                    _requests.Dequeue();
                }

                if (_requests.Count < requestLimit)
                {
                    _requests.Enqueue(now);
                    return;
                }

                waitFor = window - (now - _requests.Peek());
            }
            finally
            {
                _mutex.Release();
            }

            if (waitFor > TimeSpan.Zero)
            {
                await Task.Delay(waitFor.Value, cancellationToken);
            }
        }
    }
}
