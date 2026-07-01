namespace CeidgMirror.Infrastructure.Ceidg;

public sealed class CeidgRateLimitExceededException(string message, TimeSpan retryAfter) : InvalidOperationException(message)
{
    public TimeSpan RetryAfter { get; } = retryAfter;
}