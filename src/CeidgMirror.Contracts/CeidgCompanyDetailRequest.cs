namespace CeidgMirror.Contracts;

public sealed class CeidgCompanyDetailRequest
{
    public string? Nip { get; init; }
    public string? Regon { get; init; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Nip) && string.IsNullOrWhiteSpace(Regon))
        {
            throw new InvalidOperationException("CEIDG company detail request requires NIP or REGON.");
        }
    }
}
