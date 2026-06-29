namespace CeidgMirror.Infrastructure.Postgres;

public sealed class PostgresOptions
{
    public const string SectionName = "Postgres";

    public string ConnectionString { get; init; } = "Host=localhost;Port=5433;Database=ceidg_mirror;Username=postgres;Password=postgres";
}
