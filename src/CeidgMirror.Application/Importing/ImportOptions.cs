namespace CeidgMirror.Application.Importing;

public sealed class ImportOptions
{
    public const string SectionName = "Import";

    public bool Enabled { get; init; }
    public bool RunOnce { get; init; } = true;
    public int StartPage { get; init; } = 1;
    public int PageLimit { get; init; } = 50;
    public int MaxPages { get; init; } = 1;
    public int MaxCompanies { get; init; } = 0;
}
