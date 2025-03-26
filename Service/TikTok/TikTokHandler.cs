using OpenQA.Selenium;
using UndChrDrv;

namespace TikTokDownloader.Service.TikTok;

public interface ITikTokHandler
{
    Task Login();
}

public class TikTokHandler : ITikTokHandler
{
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
}