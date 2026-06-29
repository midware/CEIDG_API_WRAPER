namespace CeidgMirror.Domain.Companies;

public sealed record CompanyExternalId(string Value)
{
    public override string ToString() => Value;
}
