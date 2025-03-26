using Microsoft.Extensions.Hosting;
using Serilog;
using Spectre.Console;
using TikTokDownloader.Service.TikTok;

namespace TikTokDownloader.Service;

public class ConsoleMenu(ITikTokHandler handler, IHostApplicationLifetime lifetime, Style style) : IHostedService
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
        const string exit = "Выйти";
        
        while (!lifetime.ApplicationStopping.IsCancellationRequested)
        {
            var choices = new SelectionPrompt<string>()
                .Title("Выберете действие")
                .HighlightStyle(style)
                .AddChoices(auth, exit);
            
            var prompt = AnsiConsole.Prompt(choices);

            try
            {
                switch (prompt)
                {
                    case auth:
                        await handler.Login();
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