using OpenQA.Selenium;
using Spectre.Console;
using UndChrDrv;

namespace TikTokDownloader.Service.TikTok;

public interface ITikTokHandler
{
    Task Login();
    Task DownloadUser();
    string GetUsername();
}

public class TikTokHandler(Style style) : ITikTokHandler
{
    private const string SiteUrl = "https://www.tiktok.com";
    
    public async Task Login()
    {
        var drv = await ChrDrv.Create();
        if (drv == null)
        {
            throw new ApplicationException("Chrome Driver is not loaded");
        }
        await drv.Navigate().GoToUrlAsync("https://www.tiktok.com/");
        await drv.ClickElement(By.XPath("//div[contains(@class, 'NavPlaceholder')]//button[@id='header-login-button']"));
    }

    public async Task DownloadUser()
    {
        var username = GetUsername();

        var r = username;
    }
    
    public string GetUsername()
    {
        return AnsiConsole.Prompt(
            new TextPrompt<string>($"{"Введите".MarkupMainColor()} {"юзернейм (без @)".MarkupAquaColor()}")
                .PromptStyle(style)
                .ValidationErrorMessage("Некорректный юзернейм".MarkupErrorColor())
                .Validate(s => !string.IsNullOrEmpty(s)));
    }
}