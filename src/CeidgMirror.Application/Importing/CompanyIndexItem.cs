namespace CeidgMirror.Application.Importing;

public sealed record CompanyIndexItem(
    string? CeidgId,
    string? Nip,
    string? Regon,
    string? DetailLink,
    string RawJson);
