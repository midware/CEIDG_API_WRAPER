namespace CeidgMirror.Application.Importing;

public sealed class ImportOptions
{
    public const string SectionName = "Import";

    public bool Enabled { get; init; }
    public bool RunOnce { get; init; } = false;
    public string Source { get; init; } = "ChangesApi";
    public DateOnly ChangesFrom { get; init; } = new(2011, 7, 1);
    public DateOnly? ChangesTo { get; init; }
    public bool Resume { get; init; } = true;
    public bool SkipExistingCompanies { get; init; } = true;
    public int StartPage { get; init; } = 1;
    public int PageLimit { get; init; } = 50;
    public int MaxPages { get; init; } = 0;
    public int MaxCompanies { get; init; } = 0;
    public int MaxReports { get; init; } = 1;
    public string? ReportNameContains { get; init; } = "Zarejestrowane dzialalnosci";
    public string ReportFileType { get; init; } = "csv";
    public int LoopDelayMinutes { get; init; } = 60;
    public int FailureRetryDelayMinutes { get; init; } = 5;
}
