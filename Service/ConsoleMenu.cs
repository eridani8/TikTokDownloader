using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Serilog;
using Spectre.Console;
using TikTokDownloader.Service.TikTok;
using UndChrDrv;

namespace TikTokDownloader.Service;

public class ConsoleMenu(ITikTokHandler handler, IHostApplicationLifetime lifetime, Style style, ChrDrvSettings drvSettings) : IHostedService
{
    private Task? _task;
    
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _task = Worker();
        return Task.CompletedTask;
    }
    
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            lifetime.StopApplication();
            if (_task != null)
            {
                await Task.WhenAny(_task, Task.Delay(Timeout.Infinite, cancellationToken));
            }
        }
        finally
        {
            _task?.Dispose();
        }
    }

    private async Task Worker()
    {
        const string auth = "Авторизация";
        const string download = "Загрузка";
        const string openDirectory = "Открыть папку загрузок";
        const string feedback = "Обратная связь";
        const string exit = "Выйти";

        const string downloadUser = "Скачать по юзернейму";
        const string downloadTag = "Скачать по тегу";
        
        AnsiConsole.Markup("Chrome: ".MarkupPrimaryColor());
        AnsiConsole.Write(new TextPath(UndChrDrv.Extensions.FindChromePath().EscapeMarkup())
            .RootColor(Color.Yellow)
            .SeparatorColor(Color.SeaGreen1)
            .StemColor(Color.Yellow)
            .LeafColor(Color.Green));
        AnsiConsole.WriteLine();
        AnsiConsole.Markup("ChromeDriver: ".MarkupPrimaryColor());
        AnsiConsole.Write(new TextPath(drvSettings.ChromeDriverPath.EscapeMarkup())
            .RootColor(Color.Yellow)
            .SeparatorColor(Color.SeaGreen1)
            .StemColor(Color.Yellow)
            .LeafColor(Color.Green));
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine();
        
        while (!lifetime.ApplicationStopping.IsCancellationRequested)
        {
            var choices = new SelectionPrompt<string>()
                .Title("Выберете действие")
                .HighlightStyle(style)
                .AddChoices(openDirectory, auth, download, feedback, exit);
            var prompt = AnsiConsole.Prompt(choices);
            try
            {
                switch (prompt)
                {
                    case auth:
                        await handler.Login();
                        break;
                    case download:
                        var downloadChoices = new SelectionPrompt<string>()
                            .Title("Выберете действие")
                            .HighlightStyle(style)
                            .AddChoices(downloadUser, downloadTag);
                        var downloadPrompt = AnsiConsole.Prompt(downloadChoices);
                        switch (downloadPrompt)
                        {
                            case downloadUser:
                                await handler.DownloadUser();
                                break;
                            case downloadTag:
                                await handler.DownloadTag();
                                break;
                        }
                        break;
                    case openDirectory:
                        Process.Start(new ProcessStartInfo(TikTokHandler.VideosDirectory) { UseShellExecute = true });
                        break;
                    case feedback:
                        Process.Start(new ProcessStartInfo("https://t.me/eridani_8") { UseShellExecute = true });
                        break;
                    case exit:
                        lifetime.StopApplication();
                        break;
                }
            }
            catch (Exception e)
            {
                Log.ForContext<ConsoleMenu>().Error(e, "An error occurred while selecting the menu item");
            }
        }
    }
}