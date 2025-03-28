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
using UndChrDrv.ChrDrvSettings;
using WebDriverManager;
using WebDriverManager.DriverConfigs.Impl;
using WebDriverManager.Helpers;

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
    const string chromePath = "Chrome";

    var chromeDirectory = Path.Combine(Directory.GetCurrentDirectory(), chromePath);

    if (!Directory.Exists(chromePath))
    {
        Directory.CreateDirectory(chromePath);
    }

    var driverManager = new DriverManager();
    var driverPath = driverManager.SetUpDriver(new ChromeConfig(), VersionResolveStrategy.MatchingBrowser);

    if (!File.Exists(driverPath))
    {
        throw new ApplicationException("The chrome driver could not be found");
    }
    
    var profilePath = Path.Combine(chromeDirectory, "Profile");
    if (!Directory.Exists(profilePath))
    {
        Directory.CreateDirectory(profilePath);
    }
    
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
    builder.Services.AddSingleton<ChrDrvSettingsWithoutDriver>(_ => new ChrDrvSettingsWithoutDriver()
    {
        ChromeDir = chromeDirectory,
        UsernameDir = "Human",
        DriverPath = driverPath
    });
    builder.Services.AddSingleton<Style>(_ => style);
    builder.Services.AddSingleton<ITikTokHandler, TikTokHandler>();
    builder.Services.AddHostedService<ConsoleMenu>();
    
    var app = builder.Build();

    AppDomain.CurrentDomain.ProcessExit += (s, e) =>
    {
        UndChrDrv.Extensions.KillAllOpenedBrowsers();
        File.Delete("cookies.txt");
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