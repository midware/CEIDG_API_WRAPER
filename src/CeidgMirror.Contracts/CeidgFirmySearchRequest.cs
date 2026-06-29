namespace CeidgMirror.Contracts;

public sealed class CeidgFirmySearchRequest
{
    public IReadOnlyCollection<string> Nip { get; init; } = [];
    public IReadOnlyCollection<string> Regon { get; init; } = [];
    public IReadOnlyCollection<string> CompanyName { get; init; } = [];
    public IReadOnlyCollection<string> City { get; init; } = [];
    public IReadOnlyCollection<string> Voivodeship { get; init; } = [];
    public IReadOnlyCollection<string> Pkd { get; init; } = [];
    public IReadOnlyCollection<CeidgCompanyStatus> Status { get; init; } = [];
    public DateOnly? StartedFrom { get; init; }
    public DateOnly? StartedTo { get; init; }
    public int Page { get; init; } = 1;
    public int Limit { get; init; } = 50;
}
