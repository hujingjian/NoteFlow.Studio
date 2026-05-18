using System;
using System.Collections.Generic;
using System.Linq;
using NoteFlow.Studio.Service.Interfaces;

namespace NoteFlow.Studio.Service;

public class VideoParserServiceFactory
{
    private readonly IEnumerable<IVideoParserService> _parsers;

    public VideoParserServiceFactory(IEnumerable<IVideoParserService> parsers)
    {
        _parsers = parsers;
    }

    public IVideoParserService? GetParser(string url)
    {
        if (url.Contains("douyin.com"))
        {
            return _parsers.FirstOrDefault(p => p.PlatformName == "Douyin");
        }
        else if (url.Contains("bilibili.com"))
        {
            return _parsers.FirstOrDefault(p => p.PlatformName == "Bilibili");
        }
        else if (url.Contains("youtube.com") || url.Contains("youtu.be"))
        {
            return _parsers.FirstOrDefault(p => p.PlatformName == "YouTube");
        }
        else if (url.Contains("xiaohongshu.com"))
        {
            return _parsers.FirstOrDefault(p => p.PlatformName == "XiaoHongShu");
        }
        return null;
    }

    public IVideoParserService? GetParserByPlatform(string platform)
    {
        if (platform.Equals("douyin", StringComparison.OrdinalIgnoreCase))
        {
            return _parsers.FirstOrDefault(p => p.PlatformName == "Douyin");
        }
        else if (platform.Equals("bilibili", StringComparison.OrdinalIgnoreCase))
        {
            return _parsers.FirstOrDefault(p => p.PlatformName == "Bilibili");
        }
        else if (platform.Equals("youtube", StringComparison.OrdinalIgnoreCase))
        {
            return _parsers.FirstOrDefault(p => p.PlatformName == "YouTube");
        }
        else if (platform.Equals("xiaohongshu", StringComparison.OrdinalIgnoreCase))
        {
            return _parsers.FirstOrDefault(p => p.PlatformName == "XiaoHongShu");
        }
        return null;
    }
}
