using Flurl;
using Microsoft.Extensions.Hosting;
using OpenQA.Selenium;
using ParserExtension;
using ParserExtension.Helpers;
using Serilog;
using Spectre.Console;
using UndChrDrv;
using UndChrDrv.Stealth.Clients.Extensions;

namespace TikTokDownloader.Service.TikTok;

public interface ITikTokHandler
{
    Task Login();
    Task DownloadUser();
    IAsyncEnumerable<VideoDiv> ScrollAndGetUrls(ChrDrv drv, XpathSet xpathSet);
    IEnumerable<VideoDiv?> GetVideoUrls(ParserWrapper parse, XpathSet xpath);
    Task StopAtCaptcha(ChrDrv chrDrv);
    string GetUsername();
}

public class TikTokHandler(Style style, IHostApplicationLifetime lifetime) : ITikTokHandler
{
    private const string SiteUrl = "https://www.tiktok.com";
    private const string ChrDrvLoadException = "Chrome Driver could not initialize";
    
    public async Task Login()
    {
        var drv = await ChrDrv.Create();
        if (drv is null)
        {
            throw new ApplicationException(ChrDrvLoadException);
        }
        await drv.Navigate().GoToUrlAsync("https://www.tiktok.com/");
        await drv.ClickElement(By.XPath("//div[contains(@class, 'NavPlaceholder')]//button[@id='header-login-button']"));
    }

    public async Task DownloadUser()
    {
        var username = GetUsername();
        var drv = await ChrDrv.Create();
        if (drv is null)
        {
            throw new ApplicationException(ChrDrvLoadException);
        }
        var userUrl = Url.Combine(SiteUrl, $"@{username}");
        if (!Url.IsValid(userUrl))
        {
            throw new UriFormatException($"Invalid URL: {userUrl}");
        }
        await drv.Navigate().GoToUrlAsync(userUrl);
        await StopAtCaptcha(drv);
        
        var userPath = Path.Combine("videos", "users", username);
        if (!Directory.Exists(userPath))
        {
            Directory.CreateDirectory(userPath);
        }
        
        var videosContainer = await drv.GetElement(By.XPath("//div[@data-e2e='user-post-item-list']"));
        if (videosContainer is not null)
        {
            var xpathSet = new XpathSet(
                "//div[@data-e2e='user-post-item-list']/div",
                "//a[contains(@href,'/video/')]");

            await foreach (var videoDiv in ScrollAndGetUrls(drv, xpathSet))
            {
                drv.FocusAndScrollToElement(videoDiv.Xpath);
                // TODO download
            }
        }
        else
        {
            AnsiConsole.MarkupLine("");
        }
        drv.Dispose();
    }

    public async IAsyncEnumerable<VideoDiv> ScrollAndGetUrls(ChrDrv drv, XpathSet xpathSet)
    {
        var height = drv.ExecuteScript("return document.body.scrollHeight");

        var list = new HashSet<string>();
        while (!lifetime.ApplicationStopping.IsCancellationRequested)
        {
            await StopAtCaptcha(drv);
            var parse = drv.PageSource.GetParse();

            if (parse is null)
            {
                continue;
            }

            foreach (var videoDiv in GetVideoUrls(parse, xpathSet))
            {
                if (videoDiv is null || !list.Add(videoDiv.Url)) continue;
                yield return videoDiv;
            }
            
            await StopAtCaptcha(drv);
            drv.ExecuteScript("window.scrollTo(0, document.body.scrollHeight)");
            await StopAtCaptcha(drv);
            drv.SpecialWait(15000);
            var newHeight = drv.ExecuteScript("return document.body.scrollHeight");
            if (newHeight != null && newHeight.Equals(height))
            {
                drv.ExecuteScript("window.scrollTo(0, document.body.scrollHeight-4000)");
                drv.SpecialWait(2000);
                await StopAtCaptcha(drv);
                drv.ExecuteScript("window.scrollTo(0, document.body.scrollHeight)");
                drv.SpecialWait(2000);
                drv.SpecialWait(15000);
                newHeight = drv.ExecuteScript("return document.body.scrollHeight");
                
            }
        }
    }

    public IEnumerable<VideoDiv?> GetVideoUrls(ParserWrapper parse, XpathSet xpath)
    {
        var xpathCollection = parse.GetXPaths(xpath.Root);
        if (xpathCollection.Count == 0) yield return null;

        foreach (var videoXpath in xpathCollection)
        {
            var url = parse.GetAttributeValue($"{videoXpath}{xpath.Url}");
            if (string.IsNullOrEmpty(url)) continue;
            
            yield return new VideoDiv(url, videoXpath);
        }
    }

    public async Task StopAtCaptcha(ChrDrv chrDrv)
    {
        while (!lifetime.ApplicationStopping.IsCancellationRequested)
        {
            var isCaptcha = await chrDrv.GetElement(By.XPath("//div[@role='dialog' and contains(@class,'captcha_verify')]"), 5);
            if (isCaptcha == null) break;

            AnsiConsole.MarkupLine("Обнаружена каптча, пройдите и нажмите любую клавишу...".MarkupMainColor());
            Console.ReadKey(true);
            break;
        }
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