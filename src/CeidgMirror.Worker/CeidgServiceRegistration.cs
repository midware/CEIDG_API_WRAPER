using CeidgMirror.Application.Abstractions;
using CeidgMirror.Application.Importing;
using CeidgMirror.Infrastructure.Ceidg;
using CeidgMirror.Infrastructure.Importing;
using CeidgMirror.Infrastructure.Krs;
using CeidgMirror.Infrastructure.Postgres;
using Npgsql;

namespace CeidgMirror.Worker;

internal static class CeidgServiceRegistration
{
    public static IServiceCollection AddCeidgServices(this IServiceCollection services, IConfiguration configuration)
    {
        var ceidgOptions = configuration.GetSection(CeidgApiOptions.SectionName).Get<CeidgApiOptions>() ?? new CeidgApiOptions();
        var importOptions = configuration.GetSection(ImportOptions.SectionName).Get<ImportOptions>() ?? new ImportOptions();
        var krsOptions = configuration.GetSection(KrsImportOptions.SectionName).Get<KrsImportOptions>() ?? new KrsImportOptions();
        var postgresOptions = configuration.GetSection(PostgresOptions.SectionName).Get<PostgresOptions>() ?? new PostgresOptions();

        services.AddSingleton(ceidgOptions);
        services.AddSingleton(importOptions);
        services.AddSingleton(krsOptions);
        services.AddSingleton(postgresOptions);
        services.AddSingleton(new SlidingWindowRequestPacer(
            TimeSpan.FromSeconds(ceidgOptions.MinimumRequestIntervalSeconds),
            new SlidingWindowRequestPacer.Window(ceidgOptions.RequestLimit, TimeSpan.FromSeconds(ceidgOptions.WindowSeconds)),
            new SlidingWindowRequestPacer.Window(ceidgOptions.HourlyRequestLimit, TimeSpan.FromSeconds(ceidgOptions.HourlyWindowSeconds))));
        services.AddSingleton(_ => new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Max(ceidgOptions.RequestTimeoutSeconds, krsOptions.RequestTimeoutSeconds)) });
        services.AddSingleton<ICeidgClient, CeidgClient>();
        services.AddSingleton<IKrsClient, KrsClient>();
        services.AddSingleton(_ => new KrsRequestPacer(krsOptions));
        services.AddSingleton(_ => NpgsqlDataSource.Create(postgresOptions.ConnectionString));
        services.AddSingleton<ICompanyRecordStore, PostgresCompanyRecordStore>();
        services.AddSingleton<ICeidgImportService, CeidgInitialImportService>();
        services.AddSingleton<IKrsImportService, KrsImportService>();

        return services;
    }
}
