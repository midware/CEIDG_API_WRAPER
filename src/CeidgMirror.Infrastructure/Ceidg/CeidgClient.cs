using System.Net.Http.Headers;
using CeidgMirror.Application.Abstractions;
using CeidgMirror.Contracts;

namespace CeidgMirror.Infrastructure.Ceidg;

public sealed class CeidgClient(
    HttpClient httpClient,
    CeidgApiOptions options,
    SlidingWindowRequestPacer pacer) : ICeidgClient
{
    public Task<CeidgRawResponse> GetCompaniesAsync(
        CeidgFirmySearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var uri = CeidgQueryBuilder.BuildCompaniesUri(options.BaseUrl, request);
        return SendAsync(uri, cancellationToken);
    }

    public Task<CeidgRawResponse> GetCompanyDetailsAsync(
        CeidgCompanyDetailRequest request,
        CancellationToken cancellationToken = default)
    {
        var uri = CeidgQueryBuilder.BuildCompanyDetailsUri(options.BaseUrl, request);
        return SendAsync(uri, cancellationToken);
    }

    public Task<CeidgRawResponse> GetCompanyByIdAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        var uri = CeidgQueryBuilder.BuildCompanyByIdUri(options.BaseUrl, id);
        return SendAsync(uri, cancellationToken);
    }

    public Task<CeidgRawResponse> GetChangesAsync(
        DateOnly from,
        DateOnly? to = null,
        int page = 1,
        int limit = 500,
        CancellationToken cancellationToken = default)
    {
        var uri = CeidgQueryBuilder.BuildChangesUri(options.BaseUrl, from, to, page, limit);
        return SendAsync(uri, cancellationToken);
    }

    private async Task<CeidgRawResponse> SendAsync(Uri uri, CancellationToken cancellationToken)
    {
        await pacer.WaitForSlotAsync(cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrWhiteSpace(options.JwtToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.JwtToken);
        }

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        return new CeidgRawResponse(uri, response.StatusCode, content, DateTimeOffset.UtcNow);
    }
}
