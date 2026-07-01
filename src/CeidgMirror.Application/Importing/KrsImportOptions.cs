namespace CeidgMirror.Application.Importing;

public sealed class KrsImportOptions
{
    public const string SectionName = "KrsImport";

    public bool Enabled { get; init; } = false;
    public string Source { get; init; } = "SeededNumbers";
    public Uri BaseUrl { get; init; } = new("https://api-krs.ms.gov.pl/");
    public string Register { get; init; } = "P";
    public string DayFormat { get; init; } = "yyyy-MM-dd";
    public DateOnly StartDate { get; init; } = new(2021, 12, 9);
    public DateOnly? EndDate { get; init; }
    public bool Resume { get; init; } = true;
    public int MaxItems { get; init; } = 0;
    public int RequestLimit { get; init; } = 60;
    public int WindowSeconds { get; init; } = 60;
    public int MinimumRequestIntervalSeconds { get; init; } = 1;
    public int RequestTimeoutSeconds { get; init; } = 120;
    public string[] SeedKrsNumbers { get; init; } = [];
}
