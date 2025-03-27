using System.Diagnostics;
using Flurl.Http;
using Newtonsoft.Json;
using Serilog;

namespace TikTokDownloader.Service;

public static class Extensions
{
    public static string MarkupSecondaryColor(this string str)
    {
        return $"[yellow1]{str}[/]";
    }
    
    public static string MarkupPrimaryColor(this string str)
    {
        return $"[aquamarine1]{str}[/]";
    }
    
    public static string MarkupErrorColor(this string str)
    {
        return $"[red3_1]{str}[/]";
    }
}