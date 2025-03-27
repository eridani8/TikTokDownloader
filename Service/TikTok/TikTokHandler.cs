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

public enum DownloadType
{
    User,
    Tag
}

public interface ITikTokHandler
{
    Task Login();
    Task DownloadUser();
    Task DownloadTag();
    Task DownloadVideos(ChrDrv drv, DownloadType downloadType, string str, XpathSet xpathSet);
    IEnumerable<VideoDiv> ScrollAndGetUrls(ChrDrv drv, XpathSet xpathSet);
    IEnumerable<VideoDiv?> GetVideoUrls(ParserWrapper parse, XpathSet xpath);
    Task<string?> DownloadVideoFile(string url, string directoryPath);
    string GetUserString(string request);
    List<ChrDrv> Drivers { get; set; }
}

public class TikTokHandler(Style style, ChrDrvSettings drvSettings, IHostApplicationLifetime lifetime) : ITikTokHandler
{
    private const string SiteUrl = "https://www.tiktok.com";
    private Timer? _timer;
    private const int TimerPeriod = 1000;
    public const string VideosDirectory = @"F:\tt\videos"; // TODO
    public List<ChrDrv> Drivers { get; set; } = [];
    private volatile bool _captchaDetected;

    public async Task Login()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("Пройдите авторизацию, после чего нажмите любую клавишу...".MarkupPrimaryColor());
        AnsiConsole.WriteLine();

        var drv = await ChrDrvFactory.Create(drvSettings);
        Drivers.Add(drv);

        await drv.Navigate().GoToUrlAsync("https://www.tiktok.com/");
        await drv.ClickElement(
            By.XPath("//div[contains(@class, 'NavPlaceholder')]//button[@id='header-login-button']"));

        Console.ReadKey(true);

        drv.Dispose();
    }

    public async Task DownloadUser()
    {
        AnsiConsole.WriteLine();
        var username = GetUserString($"{"Введите юзернейм ".MarkupPrimaryColor()} {"без @".MarkupSecondaryColor()}");
        var drv = await ChrDrvFactory.Create(drvSettings);
        Drivers.Add(drv);

        var userUrl = Url.Combine(SiteUrl, $"@{username}");
        if (!Url.IsValid(userUrl))
        {
            throw new UriFormatException($"Invalid URL: {userUrl}");
        }

        await drv.Navigate().GoToUrlAsync(userUrl);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("Подготовка...".MarkupPrimaryColor());
        AnsiConsole.WriteLine();

        await DownloadVideos(drv, DownloadType.User, username, new XpathSet(
            "//div[@data-e2e='user-post-item-list']/div",
            "//a[contains(@href,'/video/')]"));
    }

    public async Task DownloadTag()
    {
        AnsiConsole.WriteLine();
        var tag = GetUserString($"{"Введите тег ".MarkupPrimaryColor()} {"без #".MarkupSecondaryColor()}");
        var drv = await ChrDrvFactory.Create(drvSettings);
        Drivers.Add(drv);

        var tagUrl = Url.Combine(SiteUrl, "tag", $"@{tag}");
        if (!Url.IsValid(tagUrl))
        {
            throw new UriFormatException($"Invalid URL: {tagUrl}");
        }

        await drv.Navigate().GoToUrlAsync(tagUrl);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("Подготовка...".MarkupPrimaryColor());
        AnsiConsole.WriteLine();
        
        await DownloadVideos(drv, DownloadType.Tag, tag, new XpathSet(
            "//div[@data-e2e='challenge-item']/div",
            "//a[contains(@href,'/video/')]"));
    }

    public async Task DownloadVideos(ChrDrv drv, DownloadType downloadType, string str, XpathSet xpathSet)
    {
        var subPath = downloadType switch
        {
            DownloadType.User => "users",
            DownloadType.Tag => "tags",
            _ => throw new ArgumentOutOfRangeException(nameof(downloadType), downloadType, null)
        };
        
        var path = Path.Combine(VideosDirectory, subPath, str);
        var index = 1;

        var videoContainer = downloadType switch
        {
            DownloadType.User => "//div[@data-e2e='user-post-item-list']",
            DownloadType.Tag => "//div[@data-e2e='challenge-item-list']",
            _ => throw new ArgumentOutOfRangeException(nameof(downloadType), downloadType, null)
        };

        await Task.Delay(5000);
        
        var videosContainer = await drv.GetElement(By.XPath(videoContainer), 7);
        if (videosContainer is not null)
        {
            _timer = new Timer(TimerCallback, drv, TimerPeriod, TimerPeriod);
            var downloadPolicy = Policy
                .HandleResult<string?>(result => result == null)
                .WaitAndRetryAsync(7, _ => TimeSpan.FromSeconds(3));
            var cookiesDirectory = Path.Combine(Directory.GetCurrentDirectory(), "cookies.txt");
            
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            var downloadTypeStr = downloadType switch
            {
                DownloadType.User => "юзернейма",
                DownloadType.Tag => "тега",
                _ => throw new ArgumentOutOfRangeException(nameof(downloadType), downloadType, null)
            };
            
            AnsiConsole.MarkupLine($"Загрузка {downloadTypeStr} {str.EscapeMarkup()}".MarkupPrimaryColor());
            AnsiConsole.WriteLine();

            foreach (var videoDiv in ScrollAndGetUrls(drv, xpathSet))
            {
                if (lifetime.ApplicationStopping.IsCancellationRequested) break;

                while (_captchaDetected)
                {
                    await Task.Delay(3000);
                }

                drv.FocusAndHighlightByXPath(videoDiv.Xpath, "aquamarine");
                var downloadPath = await downloadPolicy.ExecuteAsync(async () =>
                {
                    await drv.SaveCookiesToNetscapeFile(cookiesDirectory, "tiktok.com");
                    return await DownloadVideoFile(videoDiv.Url, path);
                });
                if (downloadPath != null)
                {
                    AnsiConsole.Markup($"[[{index}]] ".MarkupSecondaryColor());
                    AnsiConsole.Write(new TextPath(downloadPath.EscapeMarkup())
                        .RootColor(Color.Yellow)
                        .SeparatorColor(Color.SeaGreen1)
                        .StemColor(Color.Yellow)
                        .LeafColor(Color.Green));
                    AnsiConsole.Markup(" OK".MarkupPrimaryColor());
                    AnsiConsole.WriteLine();
                    drv.HighlightElementByXPath(videoDiv.Xpath, "yellow");
                }
                else
                {
                    AnsiConsole.MarkupLine(videoDiv.Url.MarkupErrorColor());
                    drv.HighlightElementByXPath(videoDiv.Xpath, "red");
                }

                index++;
            }

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("Загрузка ".MarkupPrimaryColor() + str.MarkupSecondaryColor() +
                                   " завершена, скачано ".MarkupPrimaryColor() +
                                   index.ToString().MarkupSecondaryColor() + " видео".MarkupPrimaryColor());
            AnsiConsole.WriteLine();
        }
        else
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("Видео на найдены".MarkupErrorColor());
        }
        
        if (_timer != null)
        {
            await _timer.DisposeAsync();
        }
        File.Delete("cookies.txt");
        // drv.Dispose(); // TODO
        AnsiConsole.WriteLine();
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
            drv.SpecialWait(2000);
            
            var newHeight = drv.ExecuteScript("return document.body.scrollHeight");

            if (newHeight != null && newHeight.Equals(previousHeight))
            {
                retriesAtBottom++;

                if (retriesAtBottom > 2)
                {
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine("В некоторых случаях, видео могут появится после задержки".MarkupSecondaryColor());
                    var confirmation = AnsiConsole.Prompt(
                        new TextPrompt<bool>("Подтвердите завершение загрузки, или продолжить поиск видео через 1 минуту".MarkupSecondaryColor())
                            .AddChoice(true)
                            .AddChoice(false)
                            .DefaultValue(true)
                            .WithConverter(choice => choice ? "y" : "n"));
                    
                    if (confirmation)
                    {
                        yield break;
                    }

                    retriesAtBottom = 0;
                    var task = AnsiConsole.Status()
                        .Spinner(Spinner.Known.Balloon)
                        .SpinnerStyle(style)
                        .StartAsync("Ожидание возобновление загрузки...", async ctx =>
                        {
                            var secondsRemaining = 60;
                            while (secondsRemaining > 0)
                            {
                                ctx.Status($"Осталось {secondsRemaining} секунд...");
                                await Task.Delay(1000);
                                secondsRemaining--;
                            }
                        });
                    task.GetAwaiter().GetResult();
                }

                drv.ExecuteScript("window.scrollTo(0, document.body.scrollHeight-4000)");
                drv.SpecialWait(2000);
                drv.ExecuteScript("window.scrollTo(0, document.body.scrollHeight)");
                drv.SpecialWait(2000);
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

        _captchaDetected = true;
        _timer?.Change(Timeout.Infinite, Timeout.Infinite);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("Обнаружена каптча, пройдите и нажмите любую клавишу...".MarkupSecondaryColor());
        AnsiConsole.WriteLine();
        Console.ReadKey(true);

        _captchaDetected = false;
        _timer?.Change(0, 1000);
    }

    public async Task<string?> DownloadVideoFile(string url, string directoryPath)
    {
        var fileName = url.Split('/').Last();
        if (string.IsNullOrEmpty(fileName)) return null;

        //var errorStringBuilder = new StringBuilder();

        var fullDirectoryPath = Path.GetFullPath(directoryPath);

        var args =
            $"""--cookies=cookies.txt --no-progress -N 7 -P "{fullDirectoryPath}" -o "{fileName}.%(ext)s" {url} """;
        var cli = Cli.Wrap("yt-dlp.exe")
            .WithArguments(args)
            .WithWorkingDirectory(Directory.GetCurrentDirectory())
            .WithValidation(CommandResultValidation.None);
            //.WithStandardErrorPipe(PipeTarget.ToStringBuilder(errorStringBuilder));

        await cli.ExecuteBufferedAsync(lifetime.ApplicationStopping);

        // var error = errorStringBuilder.ToString();
        // if (!string.IsNullOrEmpty(error))
        // {
        //     await File.WriteAllTextAsync(Path.Combine(fullDirectoryPath, $"{fileName}.error.log"), error);
        // }

        var filePath = Directory.EnumerateFiles(directoryPath)
            .FirstOrDefault(file => Path.GetFileNameWithoutExtension(file) == fileName);

        if (string.IsNullOrEmpty(filePath)) return null;

        return Path.Combine(directoryPath, Path.GetFileName(filePath));
    }

    public string GetUserString(string request)
    {
        return AnsiConsole.Prompt(
            new TextPrompt<string>(request)
                .PromptStyle(style)
                .ValidationErrorMessage("Некорректная строка".MarkupErrorColor())
                .Validate(s => !string.IsNullOrEmpty(s))).Trim();
    }
}