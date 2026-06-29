namespace CeidgMirror.Application.Importing;

public sealed class ImportOptions
{
    public const string SectionName = "Import";

    public bool Enabled { get; init; }
    public bool RunOnce { get; init; } = true;
    public string Source { get; init; } = "ReportRepository";
    public int StartPage { get; init; } = 1;
    public int PageLimit { get; init; } = 50;
    public int MaxPages { get; init; } = 1;
    public int MaxCompanies { get; init; } = 0;
    public int MaxReports { get; init; } = 1;
    public string? ReportNameContains { get; init; } = "Zarejestrowane dzialalnosci";
    public string ReportFileType { get; init; } = "csv";
}
