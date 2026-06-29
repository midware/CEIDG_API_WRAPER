using CeidgMirror.Contracts;

namespace CeidgMirror.Application.Abstractions;

public interface ICeidgClient
{
    Task<CeidgRawResponse> GetCompaniesAsync(
        CeidgFirmySearchRequest request,
        CancellationToken cancellationToken = default);

    Task<CeidgRawResponse> GetCompanyDetailsAsync(
        CeidgCompanyDetailRequest request,
        CancellationToken cancellationToken = default);

    Task<CeidgRawResponse> GetCompanyByIdAsync(
        string id,
        CancellationToken cancellationToken = default);

    Task<CeidgRawResponse> GetChangesAsync(
        DateOnly from,
        DateOnly? to = null,
        int page = 1,
        int limit = 500,
        CancellationToken cancellationToken = default);
}
