namespace CeidgMirror.Infrastructure.Ceidg;

public sealed class CeidgApiOptions
{
    public const string SectionName = "CeidgApi";

    public Uri BaseUrl { get; init; } = new("https://test-dane.biznes.gov.pl/api/ceidg/v2/");
    public Uri ReportsBaseUrl { get; init; } = new("https://dane.biznes.gov.pl/raporty/");
    public string? JwtToken { get; init; }
    public int RequestLimit { get; init; } = 50;
    public int WindowSeconds { get; init; } = 180;
    public int HourlyRequestLimit { get; init; } = 1000;
    public int HourlyWindowSeconds { get; init; } = 3600;
}
