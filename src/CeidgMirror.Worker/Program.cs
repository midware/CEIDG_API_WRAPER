using CeidgMirror.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddCeidgClient(builder.Configuration);
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
