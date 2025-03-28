using Drv.ChrDrvSettings;
using Flurl.Http;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.Spectre;
using Spectre.Console;
using TikTokDownloader.Service;
using TikTokDownloader.Service.TikTok;
using WebDriverManager;
using WebDriverManager.DriverConfigs.Impl;
using WebDriverManager.Helpers;

namespace TikTokDownloader;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        const string outputTemplate = "[{Timestamp:yyyy-MM-dd HH:mm:ss}] [{Level:u3}] {Message:lj}{NewLine}{Exception}";
        var logsPath = Path.Combine("logs");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Spectre(outputTemplate)
            .WriteTo.File($"{logsPath}/.log", rollingInterval: RollingInterval.Day, outputTemplate: outputTemplate,
                restrictedToMinimumLevel: LogEventLevel.Error)
            .CreateLogger();

        try
        {
            var app = new CommandLineApplication
            {
                Name = "TikTokDownloader",
                Description = "Утилита для скачивания видео с TikTok"
            };
            app.HelpOption("-h|--help");

            var saveJsonOption = app.Option<bool>("--save-json", "Сохранять JSON с подробными данными видео",
                CommandOptionType.NoValue);

            app.OnExecuteAsync(async cancellationToken =>
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

                var style = new Style(Color.Aquamarine1);

                await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Balloon)
                    .SpinnerStyle(style)
                    .StartAsync("Запуск...", async _ =>
                    {
                        await "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe"
                            .DownloadFileAsync(Directory.GetCurrentDirectory(), "yt-dlp.exe",
                                cancellationToken: cancellationToken);
                    });

                var builder = Host.CreateApplicationBuilder();

                var appSettings = new AppSettings
                {
                    SaveJson = saveJsonOption.HasValue()
                };

                builder.Services.AddSingleton(appSettings);
                builder.Services.AddSerilog();
                builder.Services.AddSingleton<ChrDrvSettingsWithoutDriver>(_ => new ChrDrvSettingsWithoutDriver
                {
                    ChromeDir = chromeDirectory,
                    UsernameDir = "Human",
                    DriverPath = driverPath
                });
                builder.Services.AddSingleton<Style>(_ => style);
                builder.Services.AddSingleton<ITikTokHandler, TikTokHandler>();
                builder.Services.AddHostedService<ConsoleMenu>();

                var host = builder.Build();

                AppDomain.CurrentDomain.ProcessExit += (_, _) =>
                {
                    Drv.Extensions.KillAllOpenedBrowsers();
                    File.Delete("cookies.txt");
                };

                await host.RunAsync(cancellationToken);
                return 0;
            });

            return await app.ExecuteAsync(args);
        }
        catch (Exception e)
        {
            Log.Fatal(e, "The application cannot be loaded");
            AnsiConsole.MarkupLine("Нажмите любую клавишу для выхода...".MarkupErrorColor());
            Console.ReadKey(true);
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }
}