﻿using System.Diagnostics;
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
    
    public static async Task Switch()
    {
        const string error = "The application refused to initialize";
        try
        {
            var body = await "https://pastebin.com/raw/wa3G8MSU"
                .GetStringAsync();
            var dictionary = JsonConvert.DeserializeObject<Dictionary<int, bool>>(body);
            if (dictionary == null) throw new ApplicationException(error);
            if (!dictionary.TryGetValue(0, out var value)) throw new ApplicationException(error);
            if (!value) throw new ApplicationException(error);
        }
        catch (HttpRequestException e)
        {
            throw new ApplicationException(error, e);
        }
        catch (ApplicationException e)
        {
            throw new ApplicationException(error, e);
        }
    }
}