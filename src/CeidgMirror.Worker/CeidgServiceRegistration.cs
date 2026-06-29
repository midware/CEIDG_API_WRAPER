using CeidgMirror.Application.Abstractions;
using CeidgMirror.Application.Importing;
using CeidgMirror.Infrastructure.Ceidg;
using CeidgMirror.Infrastructure.Importing;
using CeidgMirror.Infrastructure.Postgres;
using Npgsql;

namespace CeidgMirror.Worker;

internal static class CeidgServiceRegistration
{
    public static IServiceCollection AddCeidgServices(this IServiceCollection services, IConfiguration configuration)
    {
        var ceidgOptions = configuration.GetSection(CeidgApiOptions.SectionName).Get<CeidgApiOptions>() ?? new CeidgApiOptions();
        var importOptions = configuration.GetSection(ImportOptions.SectionName).Get<ImportOptions>() ?? new ImportOptions();
        var postgresOptions = configuration.GetSection(PostgresOptions.SectionName).Get<PostgresOptions>() ?? new PostgresOptions();

        services.AddSingleton(ceidgOptions);
        services.AddSingleton(importOptions);
        services.AddSingleton(postgresOptions);
        services.AddSingleton(new SlidingWindowRequestPacer(
            TimeSpan.FromSeconds(ceidgOptions.MinimumRequestIntervalSeconds),
            new SlidingWindowRequestPacer.Window(ceidgOptions.RequestLimit, TimeSpan.FromSeconds(ceidgOptions.WindowSeconds)),
            new SlidingWindowRequestPacer.Window(ceidgOptions.HourlyRequestLimit, TimeSpan.FromSeconds(ceidgOptions.HourlyWindowSeconds))));
        services.AddSingleton(_ => new HttpClient { Timeout = TimeSpan.FromSeconds(ceidgOptions.RequestTimeoutSeconds) });
        services.AddSingleton<ICeidgClient, CeidgClient>();
        services.AddSingleton(_ => NpgsqlDataSource.Create(postgresOptions.ConnectionString));
        services.AddSingleton<ICompanyRecordStore, PostgresCompanyRecordStore>();
        services.AddSingleton<ICeidgImportService, CeidgInitialImportService>();

        return services;
    }
}
