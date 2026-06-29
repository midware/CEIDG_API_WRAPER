using CeidgMirror.Api;
using CeidgMirror.Infrastructure.Ceidg;

var builder = WebApplication.CreateBuilder(args);

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
