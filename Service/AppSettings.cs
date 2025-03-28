using TikTokDownloader.Service.TikTok;

namespace TikTokDownloader.Service;

public class AppSettings
{
    public bool SaveJson { get; set; }
    public string JsonsPath { get; set; } = Path.GetFullPath($@"{TikTokHandler.VideosDirectory}\jsons");
}