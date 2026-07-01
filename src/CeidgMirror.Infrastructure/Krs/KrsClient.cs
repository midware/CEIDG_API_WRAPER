using System.Net.Http.Headers;
using CeidgMirror.Application.Abstractions;
using CeidgMirror.Application.Importing;
using CeidgMirror.Contracts;

namespace CeidgMirror.Infrastructure.Krs;

public sealed class KrsClient(
    HttpClient httpClient,
    KrsImportOptions options,
    KrsRequestPacer pacer) : IKrsClient
{
    public Task<CeidgRawResponse> GetCurrentExcerptAsync(
        string krsNumber,
        string register,
        CancellationToken cancellationToken = default)
    {
        var normalizedKrs = NormalizeKrsNumber(krsNumber);
        var normalizedRegister = string.IsNullOrWhiteSpace(register) ? options.Register : register.Trim().ToUpperInvariant();
        var uri = new Uri(options.BaseUrl, $"api/krs/OdpisAktualny/{Uri.EscapeDataString(normalizedKrs)}?rejestr={Uri.EscapeDataString(normalizedRegister)}&format=json");
        return SendJsonAsync(uri, cancellationToken);
    }

    public Task<CeidgRawResponse> GetBulletinAsync(
        DateOnly day,
        string dayFormat,
        CancellationToken cancellationToken = default)
    {
        var uri = new Uri(options.BaseUrl, $"api/Krs/Biuletyn/{Uri.EscapeDataString(day.ToString(dayFormat, System.Globalization.CultureInfo.InvariantCulture))}");
        return SendJsonAsync(uri, cancellationToken);
    }

    public Task<CeidgRawResponse> GetHourlyBulletinAsync(
        DateOnly day,
        string dayFormat,
        int hourFrom,
        int hourTo,
        CancellationToken cancellationToken = default)
    {
        var dayValue = Uri.EscapeDataString(day.ToString(dayFormat, System.Globalization.CultureInfo.InvariantCulture));
        var uri = new Uri(options.BaseUrl, $"api/Krs/BiuletynGodzinowy/{dayValue}?godzinaOd={hourFrom:00}&godzinaDo={hourTo:00}");
        return SendJsonAsync(uri, cancellationToken);
    }

    private async Task<CeidgRawResponse> SendJsonAsync(Uri uri, CancellationToken cancellationToken)
    {
        await pacer.WaitForSlotAsync(cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return new CeidgRawResponse(uri, response.StatusCode, content, DateTimeOffset.UtcNow, response.Content.Headers.ContentType?.ToString());
    }

    private static string NormalizeKrsNumber(string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        return digits.PadLeft(10, '0');
    }
}
