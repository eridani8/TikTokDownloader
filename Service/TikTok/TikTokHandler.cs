using System.Text;
using CliWrap;
using CliWrap.Buffered;
using Flurl;
using Microsoft.Extensions.Hosting;
using OpenQA.Selenium;
using ParserExtension;
using ParserExtension.Helpers;
using Polly;
using Polly.Retry;
using Spectre.Console;
using UndChrDrv;
using UndChrDrv.Stealth.Clients.Extensions;

namespace TikTokDownloader.Service.TikTok;

public interface ITikTokHandler
{
    Task Login();
    Task DownloadUser();
    IEnumerable<VideoDiv> ScrollAndGetUrls(ChrDrv drv, XpathSet xpathSet);
    IEnumerable<VideoDiv?> GetVideoUrls(ParserWrapper parse, XpathSet xpath);
    Task<string?> DownloadVideoFile(string url, string directoryPath);
    string GetUsername();
}

public class TikTokHandler(Style style, ChrDrvSettings drvSettings, IHostApplicationLifetime lifetime) : ITikTokHandler
{
    private const string SiteUrl = "https://www.tiktok.com";
    private const string ChrDrvLoadException = "Chrome Driver could not initialize";
    private readonly AsyncRetryPolicy<string?> _downloadPolicy = Policy
        .HandleResult<string?>(result => result == null)
        .WaitAndRetryAsync(7, _ => TimeSpan.FromSeconds(3));
    private Timer? _timer;
    private const int TimerPeriod = 1000;
    private const string VideosDirectory = @"F:\tt\videos";

    public async Task Login()
    {
        var drv = await ChrDrv.Create(drvSettings);
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
        var drv = await ChrDrv.Create(drvSettings);
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

        AnsiConsole.MarkupLine("Подготовка...".MarkupMainColor());
        
        var userPath = Path.Combine(VideosDirectory, "users", username);
        var index = 1;
        var videosContainer = await drv.GetElement(By.XPath("//div[@data-e2e='user-post-item-list']"), 5);
        if (videosContainer is not null)
        {
            _timer = new Timer(TimerCallback, drv, TimerPeriod, TimerPeriod);
            
            if (!Directory.Exists(userPath))
            {
                Directory.CreateDirectory(userPath);
            }
            
            var xpathSet = new XpathSet(
                "//div[@data-e2e='user-post-item-list']/div",
                "//a[contains(@href,'/video/')]");
            
            var cookiesDirectory = Path.Combine(Directory.GetCurrentDirectory(), "cookies.txt");

            foreach (var videoDiv in ScrollAndGetUrls(drv, xpathSet))
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
                    AnsiConsole.Markup($"[{index}] ".EscapeMarkup().MarkupMainColor());
                    AnsiConsole.Write(new TextPath(path.EscapeMarkup())
                        .RootColor(Color.Yellow)
                        .SeparatorColor(Color.SeaGreen1)
                        .StemColor(Color.Yellow)
                        .LeafColor(Color.Green));
                    AnsiConsole.Markup(" OK".MarkupAquaColor());
                    AnsiConsole.WriteLine();
                }
                else
                {
                    AnsiConsole.MarkupLine(videoDiv.Url.MarkupErrorColor());
                }

                index++;
            }
        }
        else
        {
            AnsiConsole.MarkupLine("Видео на найдены".MarkupErrorColor());
        }

        if (_timer != null)
        {
            await _timer.DisposeAsync();
        }
        File.Delete("cookies.txt");
        drv.Dispose();
    }

    public IEnumerable<VideoDiv> ScrollAndGetUrls(ChrDrv drv, XpathSet xpathSet)
    {
        var list = new HashSet<string>();
        var retriesAtBottom = 0;

        while (!lifetime.ApplicationStopping.IsCancellationRequested)
        {
            var parse = drv.PageSource.GetParse();
            if (parse is null) continue;

            foreach (var videoDiv in GetVideoUrls(parse, xpathSet))
            {
                if (lifetime.ApplicationStopping.IsCancellationRequested) break;
                if (videoDiv is null || !list.Add(videoDiv.Url)) continue;

                yield return videoDiv;
            }

            if (lifetime.ApplicationStopping.IsCancellationRequested) break;

            var previousHeight = drv.ExecuteScript("return document.body.scrollHeight");
            drv.ExecuteScript("window.scrollTo(0, document.body.scrollHeight)");
            drv.SpecialWait(3000);
            var newHeight = drv.ExecuteScript("return document.body.scrollHeight");

            if (newHeight != null && newHeight.Equals(previousHeight))
            {
                retriesAtBottom++;

                if (retriesAtBottom > 2)
                {
                    yield break;
                }

                drv.ExecuteScript("window.scrollTo(0, document.body.scrollHeight-4000)");
                drv.SpecialWait(2000);
                drv.ExecuteScript("window.scrollTo(0, document.body.scrollHeight)");
                drv.SpecialWait(3000);
            }
            else
            {
                retriesAtBottom = 0;
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

    private void TimerCallback(object? state)
    {
        if (state is not ChrDrv drv) return;
        var parse = drv.PageSource.GetParse();
        var hasCaptcha = parse?.GetNodeByXPath("//div[contains(@class,'captcha-verify')]");
        if (hasCaptcha == null) return;

        _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        Console.WriteLine("Обнаружена каптча, пройдите и нажмите любую клавишу...");
        Console.ReadKey(true);
        _timer?.Change(0, 1000);
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
        
        await cli.ExecuteBufferedAsync(lifetime.ApplicationStopping);
        
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