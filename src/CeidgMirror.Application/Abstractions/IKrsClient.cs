using CeidgMirror.Contracts;

namespace CeidgMirror.Application.Abstractions;

public interface IKrsClient
{
    Task<CeidgRawResponse> GetCurrentExcerptAsync(
        string krsNumber,
        string register,
        CancellationToken cancellationToken = default);

    Task<CeidgRawResponse> GetBulletinAsync(
        DateOnly day,
        string dayFormat,
        CancellationToken cancellationToken = default);

    Task<CeidgRawResponse> GetHourlyBulletinAsync(
        DateOnly day,
        string dayFormat,
        int hourFrom,
        int hourTo,
        CancellationToken cancellationToken = default);
}
