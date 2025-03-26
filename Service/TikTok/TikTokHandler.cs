using System.Text;
using CliWrap;
using CliWrap.Buffered;
using CliWrap.EventStream;
using Flurl;
using Flurl.Http;
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
    Task<string?> DownloadVideoFile(string url, string directoryPath);
    string GetUsername();
}

public class TikTokHandler(Style style, IHostApplicationLifetime lifetime) : ITikTokHandler
{
    private const string SiteUrl = "https://www.tiktok.com";
    private const string ChrDrvLoadException = "Chrome Driver could not initialize";
    // private readonly Dictionary<string, string> _headers = new()
    // {
    //     { "sec-ch-ua", """ Chromium";v="134", "Not:A-Brand";v="24", "Google Chrome";v="134" """ },
    //     { "sec-ch-ua-mobile", "?0" },
    //     { "sec-ch-ua-platform", """ "Windows" """ },
    //     { "sec-fetch-dest", "document" },
    //     { "sec-fetch-mode", "navigate" },
    //     { "sec-fetch-site", "none" },
    //     { "sec-fetch-user", "?1" },
    //     { "upgrade-insecure-requests", "1" },
    //     { "user-agent", "insomnia/11.0.0, Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/134.0.0.0 Safari/537.36" },
    // };
    // private Dictionary<string, string> _cookies = new();

    public async Task Login()
    {
        var drv = await ChrDrv.Create();
        if (drv is null)
        {
            throw new ApplicationException(ChrDrvLoadException);
        }

        await drv.Navigate().GoToUrlAsync("https://www.tiktok.com/");
        await drv.ClickElement(
            By.XPath("//div[contains(@class, 'NavPlaceholder')]//button[@id='header-login-button']"));
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
                // _cookies = drv.GetCookiesAsDictionary();
                var path = await DownloadVideoFile(videoDiv.Url, userPath);
                if (path != null)
                {
                    AnsiConsole.Write(new TextPath(path.EscapeMarkup())
                        .RootColor(Color.Yellow)
                        .SeparatorColor(Color.SeaGreen1)
                        .StemColor(Color.Yellow)
                        .LeafColor(Color.Green));
                    AnsiConsole.WriteLine();
                }
                else
                {
                    AnsiConsole.WriteLine(videoDiv.Url.MarkupErrorColor());
                }
            }
        }
        else
        {
            AnsiConsole.MarkupLine("Видео на найдены".MarkupErrorColor());
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
            drv.SpecialWait(7000);
            var newHeight = drv.ExecuteScript("return document.body.scrollHeight");
            if (newHeight != null && newHeight.Equals(height))
            {
                drv.ExecuteScript("window.scrollTo(0, document.body.scrollHeight-4000)");
                drv.SpecialWait(2000);
                await StopAtCaptcha(drv);
                drv.ExecuteScript("window.scrollTo(0, document.body.scrollHeight)");
                drv.SpecialWait(2000);
                drv.SpecialWait(7000);
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
            var isCaptcha =
                await chrDrv.GetElement(By.XPath("//div[@role='dialog' and contains(@class,'captcha_verify')]"), 5);
            if (isCaptcha == null) break;

            AnsiConsole.MarkupLine("Обнаружена каптча, пройдите и нажмите любую клавишу...".MarkupMainColor());
            Console.ReadKey(true);
            break;
        }
    }

    public async Task<string?> DownloadVideoFile(string url, string directoryPath)
    {
        var fileName = url.Split('/').Last();
        if (string.IsNullOrEmpty(fileName)) return null;

        var args = $"""--no-progress -N 7 -P "{Path.GetFullPath(directoryPath)}" -o "{fileName}.%(ext)s" {url} """;
        var cli = Cli.Wrap("yt-dlp.exe")
            .WithArguments(args)
            .WithValidation(CommandResultValidation.None);
        
        await cli.ExecuteBufferedAsync();

        // var getUrl = await url
        //     .WithHeaders(_headers)
        //     .WithCookies(_cookies)
        //     .GetStringAsync();
        //
        // var directory = Path.GetFullPath(directoryPath);
        // await url
        //     .WithHeaders(_headers)
        //     .WithCookies(_cookies)
        //     .DownloadFileAsync(directory, $"{fileName}.mp4");

        var filePath = Directory.EnumerateFiles(directoryPath)
            .FirstOrDefault(file => Path.GetFileNameWithoutExtension(file) == fileName);

        if (string.IsNullOrEmpty(filePath)) return null;

        return Path.Combine(directoryPath, Path.GetFileName(filePath));
    }

    public string GetUsername()
    {
        return AnsiConsole.Prompt(
            new TextPrompt<string>($"{"Введите".MarkupMainColor()} {"юзернейм (без @)".MarkupAquaColor()}")
                .PromptStyle(style)
                .ValidationErrorMessage("Некорректный юзернейм".MarkupErrorColor())
                .Validate(s => !string.IsNullOrEmpty(s))).Trim();
    }
}