using CeidgMirror.Application.Abstractions;
using CeidgMirror.Infrastructure.Ceidg;

namespace CeidgMirror.Worker;

internal static class CeidgServiceRegistration
{
    public static IServiceCollection AddCeidgClient(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(CeidgApiOptions.SectionName).Get<CeidgApiOptions>() ?? new CeidgApiOptions();

        services.AddSingleton(options);
        services.AddSingleton(new SlidingWindowRequestPacer(
            options.RequestLimit,
            TimeSpan.FromSeconds(options.WindowSeconds)));
        services.AddSingleton<HttpClient>();
        services.AddSingleton<ICeidgClient, CeidgClient>();

        return services;
    }
}
