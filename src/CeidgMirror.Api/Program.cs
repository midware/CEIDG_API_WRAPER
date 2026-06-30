using CeidgMirror.Api;
using CeidgMirror.Infrastructure.Ceidg;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);
AddLocalSettings(builder.Configuration, "CeidgMirror.Api");
builder.Configuration.AddEnvironmentVariables();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "leadbase.network API",
        Version = "v1",
        Description = "Authenticated API for querying leadbase.network company data with token-based billing."
    });
    options.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Description = "Paste your API key. It is sent as the X-Api-Key header.",
        Name = "X-Api-Key",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey
    });
});

builder.Services.AddCeidgClient(builder.Configuration);
builder.Services.AddProductApi(builder.Configuration);

var app = builder.Build();

app.UseStaticFiles();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "leadbase.network API v1");
    options.RoutePrefix = "swagger";
});
app.MapOpenApi();
app.MapProductApi();
app.MapLeadbaseSite();

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

