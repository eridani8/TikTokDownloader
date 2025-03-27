using System.Diagnostics;
using Flurl.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.Spectre;
using Spectre.Console;
using TikTokDownloader.Service;
using TikTokDownloader.Service.TikTok;
using UndChrDrv;
using Extensions = TikTokDownloader.Service.Extensions;

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
    await Extensions.Switch();

    var style = new Style(Color.Aquamarine1);
    
    await AnsiConsole.Status()
        .Spinner(Spinner.Known.Balloon)
        .SpinnerStyle(style)
        .StartAsync("Запуск...", async _ =>
        {
            await "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe"
                .DownloadFileAsync(Directory.GetCurrentDirectory(), "yt-dlp.exe");
        });
    
    
    
    var builder = Host.CreateApplicationBuilder();

    builder.Services.AddSerilog();
    builder.Services.AddSingleton<ChrDrvSettings>(_ => new ChrDrvSettings()
    {
        ChromeDir = Path.Combine(@"H:\Chrome"), // TODO Directory.GetCurrentDirectory() // "Chrome"
        UsernameDir = "ReallyRealUser"
    });
    builder.Services.AddSingleton<Style>(_ => style);
    builder.Services.AddSingleton<ITikTokHandler, TikTokHandler>();
    builder.Services.AddHostedService<ConsoleMenu>();
    
    var app = builder.Build();

    AppDomain.CurrentDomain.ProcessExit += (s, e) =>
    {
        File.Delete("cookies.txt");
        if (app.Services.GetRequiredService<ITikTokHandler>() is not { } handler) return;
        foreach (var drv in handler.Drivers)
        {
            drv.Dispose();
        }
    };
    
    await app.RunAsync();
}
catch (Exception e)
{
    Log.Fatal(e, "The application cannot be loaded");
    AnsiConsole.MarkupLine("Нажмите любую клавишу для выхода...".MarkupErrorColor());
    Console.ReadKey(true);
}
finally
{
    await Log.CloseAndFlushAsync();
}