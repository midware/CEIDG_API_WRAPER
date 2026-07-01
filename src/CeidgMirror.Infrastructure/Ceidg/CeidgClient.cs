using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using CeidgMirror.Application.Abstractions;
using CeidgMirror.Contracts;

namespace CeidgMirror.Infrastructure.Ceidg;

public sealed partial class CeidgClient(
    HttpClient httpClient,
    CeidgApiOptions options,
    SlidingWindowRequestPacer pacer) : ICeidgClient
{
    private readonly SemaphoreSlim reportTokenLock = new(1, 1);
    private string? reportRepositoryRequestVerificationToken;

    public Task<CeidgRawResponse> GetCompaniesAsync(
        CeidgFirmySearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var uri = CeidgQueryBuilder.BuildCompaniesUri(options.BaseUrl, request);
        return SendJsonApiAsync(uri, cancellationToken);
    }

    public Task<CeidgRawResponse> GetCompanyDetailsAsync(
        CeidgCompanyDetailRequest request,
        CancellationToken cancellationToken = default)
    {
        var uri = CeidgQueryBuilder.BuildCompanyDetailsUri(options.BaseUrl, request);
        return SendJsonApiAsync(uri, cancellationToken);
    }

    public Task<CeidgRawResponse> GetCompanyByIdAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        var uri = CeidgQueryBuilder.BuildCompanyByIdUri(options.BaseUrl, id);
        return SendJsonApiAsync(uri, cancellationToken);
    }

    public Task<CeidgRawResponse> GetReportsAsync(
        DateOnly? from = null,
        DateOnly? to = null,
        CancellationToken cancellationToken = default)
    {
        var uri = CeidgQueryBuilder.BuildReportsUri(options.BaseUrl, from, to);
        return SendJsonApiAsync(uri, cancellationToken);
    }

    public Task<CeidgRawResponse> GetReportByIdAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        var uri = CeidgQueryBuilder.BuildReportByIdUri(options.BaseUrl, id);
        return SendJsonApiAsync(uri, cancellationToken);
    }

    public async Task<CeidgRawResponse> GetReportRepositoryCatalogAsync(CancellationToken cancellationToken = default)
    {
        var token = await GetReportRepositoryTokenAsync(cancellationToken);
        var uri = new Uri(options.ReportsBaseUrl, "reportsRepository?handler=Reports");
        return await SendReportRepositoryAsync(uri, token, "application/json", cancellationToken);
    }

    public async Task<CeidgRawResponse> DownloadReportFromRepositoryAsync(
        string generatedReportId,
        string? reportSubtitle = null,
        CancellationToken cancellationToken = default)
    {
        var token = await GetReportRepositoryTokenAsync(cancellationToken);
        var query = $"reportsRepository?handler=DownloadReport&generatedReportId={Uri.EscapeDataString(generatedReportId)}";
        if (!string.IsNullOrWhiteSpace(reportSubtitle) && !string.Equals(reportSubtitle, "EMPTY", StringComparison.OrdinalIgnoreCase))
        {
            query += $"&reportSubtitle={Uri.EscapeDataString(reportSubtitle)}";
        }

        var uri = new Uri(options.ReportsBaseUrl, query);
        return await SendReportRepositoryAsync(uri, token, "*/*", cancellationToken);
    }

    public Task<CeidgRawResponse> GetChangesAsync(
        DateOnly from,
        DateOnly? to = null,
        int page = 1,
        int limit = 500,
        CancellationToken cancellationToken = default)
    {
        var uri = CeidgQueryBuilder.BuildChangesUri(options.BaseUrl, from, to, page, limit);
        return SendJsonApiAsync(uri, cancellationToken);
    }

    private async Task<CeidgRawResponse> SendJsonApiAsync(Uri uri, CancellationToken cancellationToken)
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

        return new CeidgRawResponse(uri, response.StatusCode, content, DateTimeOffset.UtcNow, response.Content.Headers.ContentType?.ToString(), ReadRetryAfter(response));
    }

    private async Task<CeidgRawResponse> SendReportRepositoryAsync(
        Uri uri,
        string requestVerificationToken,
        string accept,
        CancellationToken cancellationToken)
    {
        await pacer.WaitForSlotAsync(cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.ParseAdd(accept);
        request.Headers.Add("RequestVerificationToken", requestVerificationToken);
        request.Headers.Referrer = new Uri(options.ReportsBaseUrl, "repozytorium");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        return new CeidgRawResponse(uri, response.StatusCode, content, DateTimeOffset.UtcNow, response.Content.Headers.ContentType?.ToString(), ReadRetryAfter(response));
    }

    private static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is not null)
        {
            return retryAfter.Delta;
        }

        if (retryAfter?.Date is not null)
        {
            var delay = retryAfter.Date.Value - DateTimeOffset.UtcNow;
            return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
        }

        return null;
    }

    private async Task<string> GetReportRepositoryTokenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(reportRepositoryRequestVerificationToken))
        {
            return reportRepositoryRequestVerificationToken;
        }

        await reportTokenLock.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrWhiteSpace(reportRepositoryRequestVerificationToken))
            {
                return reportRepositoryRequestVerificationToken;
            }

            var repositoryUri = new Uri(options.ReportsBaseUrl, "repozytorium");
            await pacer.WaitForSlotAsync(cancellationToken);

            using var response = await httpClient.GetAsync(repositoryUri, cancellationToken);
            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"CEIDG report repository page failed with status {(int)response.StatusCode}. Body: {Truncate(html, 500)}");
            }

            var match = RequestVerificationTokenRegex().Match(html);
            if (!match.Success)
            {
                throw new InvalidOperationException("CEIDG report repository page did not contain __RequestVerificationToken.");
            }

            reportRepositoryRequestVerificationToken = WebUtility.HtmlDecode(match.Groups[1].Value);
            return reportRepositoryRequestVerificationToken;
        }
        finally
        {
            reportTokenLock.Release();
        }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    [GeneratedRegex("name=\\\"__RequestVerificationToken\\\"[^>]*value=\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RequestVerificationTokenRegex();
}
