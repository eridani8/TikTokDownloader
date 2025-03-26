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
using Polly;
using Polly.Retry;
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
    private readonly AsyncRetryPolicy<string?> _downloadPolicy = Policy
        .HandleResult<string?>(result => result == null)
        .WaitAndRetryAsync(7, _ => TimeSpan.FromSeconds(3));

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
        
        var videosContainer = await drv.GetElement(By.XPath("//div[@data-e2e='user-post-item-list']"));
        if (videosContainer is not null)
        {
            if (!Directory.Exists(userPath))
            {
                Directory.CreateDirectory(userPath);
            }
            
            var xpathSet = new XpathSet(
                "//div[@data-e2e='user-post-item-list']/div",
                "//a[contains(@href,'/video/')]");
            
            var cookiesDirectory = Path.Combine(Directory.GetCurrentDirectory(), "cookies.txt");

            await foreach (var videoDiv in ScrollAndGetUrls(drv, xpathSet))
            {
                if (lifetime.ApplicationStopping.IsCancellationRequested) break;
                drv.FocusAndScrollToElement(videoDiv.Xpath);
                var path = await _downloadPolicy.ExecuteAsync(async () =>
                {
                    await drv.SaveCookiesToNetscapeFile(cookiesDirectory, "tiktok.com");
                    return await DownloadVideoFile(videoDiv.Url, userPath);
                });
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
                    AnsiConsole.MarkupLine(videoDiv.Url.MarkupErrorColor());
                }
            }
        }
        else
        {
            AnsiConsole.MarkupLine("Видео на найдены".MarkupErrorColor());
        }
        
        File.Delete("cookies.txt");
        drv.Dispose();
    }

    public async IAsyncEnumerable<VideoDiv> ScrollAndGetUrls(ChrDrv drv, XpathSet xpathSet)
    {
        var height = drv.ExecuteScript("return document.body.scrollHeight");

        var list = new HashSet<string>();
        while (!lifetime.ApplicationStopping.IsCancellationRequested)
        {
            await StopAtCaptcha(drv); // *
            var parse = drv.PageSource.GetParse();

            if (parse is null)
            {
                continue;
            }

            foreach (var videoDiv in GetVideoUrls(parse, xpathSet))
            {
                if (lifetime.ApplicationStopping.IsCancellationRequested) break;
                if (videoDiv is null || !list.Add(videoDiv.Url)) continue;
                yield return videoDiv;
            }

            if (lifetime.ApplicationStopping.IsCancellationRequested) break;
            
            await StopAtCaptcha(drv); // *
            drv.ExecuteScript("window.scrollTo(0, document.body.scrollHeight)");
            await StopAtCaptcha(drv); // *
            drv.SpecialWait(5000);
            var newHeight = drv.ExecuteScript("return document.body.scrollHeight");
            if (newHeight != null && newHeight.Equals(height))
            {
                drv.ExecuteScript("window.scrollTo(0, document.body.scrollHeight-4000)");
                drv.SpecialWait(2000);
                await StopAtCaptcha(drv); // *
                drv.ExecuteScript("window.scrollTo(0, document.body.scrollHeight)");
                drv.SpecialWait(5000);
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
                await chrDrv.GetElement(By.XPath("//div[@role='dialog' and contains(@class,'captcha_verify')]"), 3);
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

        var errorStringBuilder = new StringBuilder();

        var fullDirectoryPath = Path.GetFullPath(directoryPath);
        
        var args = $"""--cookies=cookies.txt --no-progress -N 7 -P "{fullDirectoryPath}" -o "{fileName}.%(ext)s" {url} """;
        var cli = Cli.Wrap("yt-dlp.exe")
            .WithArguments(args)
            .WithWorkingDirectory(Directory.GetCurrentDirectory())
            .WithValidation(CommandResultValidation.None)
            .WithStandardErrorPipe(PipeTarget.ToStringBuilder(errorStringBuilder));
        
        await cli.ExecuteBufferedAsync();
        
        var error = errorStringBuilder.ToString();
        if (!string.IsNullOrEmpty(error))
        {
            await File.WriteAllTextAsync(Path.Combine(fullDirectoryPath, $"{fileName}.error.log"), error);
        }

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