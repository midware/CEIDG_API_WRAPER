using CeidgMirror.Application.Importing;
using CeidgMirror.Worker;

var builder = Host.CreateApplicationBuilder(args);
AddLocalSettings(builder.Configuration, "CeidgMirror.Worker");
builder.Configuration.AddEnvironmentVariables();

builder.Services.AddCeidgServices(builder.Configuration);
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();

static void AddLocalSettings(IConfigurationBuilder configuration, string projectName)
{
    var candidates = new[]
    {
        Path.Combine(AppContext.BaseDirectory, "appsettings.Local.json"),
        Path.Combine(Directory.GetCurrentDirectory(), "appsettings.Local.json"),
        Path.Combine(Directory.GetCurrentDirectory(), "src", projectName, "appsettings.Local.json"),
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "appsettings.Local.json")
    };

    foreach (var path in candidates.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
    {
        configuration.AddJsonFile(path, optional: true, reloadOnChange: true);
    }
}
