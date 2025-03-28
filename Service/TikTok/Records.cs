namespace TikTokDownloader.Service.TikTok;

public record XpathSet(string Root, string Url);
public record VideoDiv(string Url, long VideoId, string Xpath);