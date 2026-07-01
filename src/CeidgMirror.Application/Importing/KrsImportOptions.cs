namespace CeidgMirror.Application.Importing;

public sealed class KrsImportOptions
{
    public const string SectionName = "KrsImport";

    public bool Enabled { get; init; } = false;
    public string Source { get; init; } = "SeededNumbers";
    public Uri BaseUrl { get; init; } = new("https://api-krs.ms.gov.pl/");
    public string Register { get; init; } = "P";
    public string DayFormat { get; init; } = "yyyy-MM-dd";
    public DateOnly StartDate { get; init; } = new(2022, 3, 8);
    public DateOnly? EndDate { get; init; }
    public bool Resume { get; init; } = true;
    public int MaxItems { get; init; } = 0;
    public int RequestLimit { get; init; } = 30;
    public int WindowSeconds { get; init; } = 60;
    public int HourlyRequestLimit { get; init; } = 500;
    public int HourlyWindowSeconds { get; init; } = 3600;
    public int MinimumRequestIntervalSeconds { get; init; } = 2;
    public int RequestTimeoutSeconds { get; init; } = 120;
    public int LoopDelayMinutes { get; init; } = 60;
    public int FailureRetryDelayMinutes { get; init; } = 5;
    public int TransientBackoffBaseSeconds { get; init; } = 30;
    public int TransientBackoffMaxSeconds { get; init; } = 900;
    public string[] SeedKrsNumbers { get; init; } = [];
}
