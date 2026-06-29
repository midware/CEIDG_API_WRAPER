using System.Net;

namespace CeidgMirror.Contracts;

public sealed record CeidgRawResponse(
    Uri RequestUri,
    HttpStatusCode StatusCode,
    string Content,
    DateTimeOffset FetchedAtUtc,
    string? ContentType = null)
{
    public bool IsSuccess => (int)StatusCode is >= 200 and <= 299;
}
