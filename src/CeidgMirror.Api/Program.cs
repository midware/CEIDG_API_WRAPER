using CeidgMirror.Api;
using CeidgMirror.Infrastructure.Ceidg;

var builder = WebApplication.CreateBuilder(args);
AddLocalSettings(builder.Configuration, "CeidgMirror.Api");
builder.Configuration.AddEnvironmentVariables();

builder.Services.AddOpenApi();
builder.Services.AddCeidgClient(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/health", () => Results.Ok(new
{
    service = "CeidgMirror.Api",
    status = "ok",
    checkedAtUtc = DateTimeOffset.UtcNow
}));

app.MapGet("/ceidg/config", (CeidgApiOptions options) => Results.Ok(new
{
    baseUrl = options.BaseUrl,
    hasJwtToken = !string.IsNullOrWhiteSpace(options.JwtToken),
    requestLimit = options.RequestLimit,
    windowSeconds = options.WindowSeconds
}));

app.Run();

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
