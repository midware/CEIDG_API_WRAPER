using CeidgMirror.Application.Abstractions;
using CeidgMirror.Infrastructure.Ceidg;

namespace CeidgMirror.Api;

internal static class CeidgServiceRegistration
{
    public static IServiceCollection AddCeidgClient(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(CeidgApiOptions.SectionName).Get<CeidgApiOptions>() ?? new CeidgApiOptions();

        services.AddSingleton(options);
        services.AddSingleton(new SlidingWindowRequestPacer(
            new SlidingWindowRequestPacer.Window(options.RequestLimit, TimeSpan.FromSeconds(options.WindowSeconds)),
            new SlidingWindowRequestPacer.Window(options.HourlyRequestLimit, TimeSpan.FromSeconds(options.HourlyWindowSeconds))));
        services.AddSingleton<HttpClient>();
        services.AddSingleton<ICeidgClient, CeidgClient>();

        return services;
    }
}
