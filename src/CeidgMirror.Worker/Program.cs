using CeidgMirror.Application.Importing;
using CeidgMirror.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddCeidgServices(builder.Configuration);
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
