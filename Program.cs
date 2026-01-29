using CollectDataAudio;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "CollectDataAudio";
});

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();