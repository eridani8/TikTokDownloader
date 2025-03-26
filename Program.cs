using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.Spectre;
using Spectre.Console;
using TikTokDownloader.Service;
using TikTokDownloader.Service.TikTok;
using UndChrDrv;

const string outputTemplate = "[{Timestamp:yyyy-MM-dd HH:mm:ss}] [{Level:u3}] {Message:lj}{NewLine}{Exception}";
var logsPath = Path.Combine("logs");

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Spectre(outputTemplate)
    .WriteTo.File($"{logsPath}/.log", rollingInterval: RollingInterval.Day, outputTemplate: outputTemplate, restrictedToMinimumLevel: LogEventLevel.Error)
    .CreateLogger();
    
try
{
    ChrDrvSettings.ChromeDir = Path.Combine(@"H:\Chrome"); // TODO Directory.GetCurrentDirectory() // "Chrome"
    ChrDrvSettings.UsernameDir = "ReallyRealUser";
    
    var builder = Host.CreateApplicationBuilder();

    builder.Services.AddSerilog();
    builder.Services.AddSingleton<Style>(_ => new Style(Color.MediumOrchid3));
    builder.Services.AddSingleton<ITikTokHandler, TikTokHandler>();
    builder.Services.AddHostedService<ConsoleMenu>();
    
    var app = builder.Build();
    
    await app.RunAsync();
}
catch (Exception e)
{
    Log.Fatal(e, "The application cannot be loaded");
}
finally
{
    await Log.CloseAndFlushAsync();
}