using CefSharp;
using NoteFlow.Studio.CefSharpHandlers;
using NoteFlow.Studio.Service;
using NoteFlow.Studio.Service.Interfaces;
using NoteFlow.Studio.Service.Models;
using NoteFlow.Studio.Service.Providers;
using NoteFlow.Studio.Service.VideoParsers;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace NoteFlow.Studio.Services
{
    public sealed class BrowserVideoMetadataService
    {
        private readonly VideoParserServiceFactory _videoParserFactory;

        public BrowserVideoMetadataService()
        {
            var playwrightProvider = new PlaywrightProvider();
            _videoParserFactory = new VideoParserServiceFactory(new IVideoParserService[]
            {
                new DouyinVideoParserService(playwrightProvider),
                new BilibiliVideoParserService(playwrightProvider),
                new XiaoHongShuVideoParserService(playwrightProvider),
                new YouTubeVideoParserService()
            });
        }

        public bool TryCreateRequest(string address, [NotNullWhen(true)] out BrowserVideoRequest? request)
        {
            request = null;
            var platform = DetectPlatform(address);
            if (string.IsNullOrEmpty(platform))
            {
                return false;
            }

            var videoId = ExtractVideoId(address, platform);
            if (string.IsNullOrEmpty(videoId))
            {
                request = new BrowserVideoRequest(platform, string.Empty, address);
                return true;
            }

            request = new BrowserVideoRequest(platform, videoId, address);
            return true;
        }

        public async Task<VideoInfo?> FetchVideoDetailAsync(BrowserVideoRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.VideoId))
            {
                return null;
            }

            var parser = _videoParserFactory.GetParserByPlatform(request.Platform);
            if (parser == null)
            {
                return null;
            }

            var cookies = await ReadPlatformCookiesAsync(request.Platform);
            return await parser.ExtractVideoDetailAsync(request.VideoId, cookies, request.Address);
        }

        private static async Task<CookieDto[]> ReadPlatformCookiesAsync(string platform)
        {
            var cookieManager = Cef.GetGlobalCookieManager();
            var visitor = new CookieVisitor();
            cookieManager.VisitAllCookies(visitor);
            var cookies = await visitor.Task;

            return cookies
                .Where(c => !string.IsNullOrEmpty(c.Domain) &&
                            c.Domain.Contains(platform, StringComparison.OrdinalIgnoreCase))
                .Select(c =>
                {
                    var domain = c.Domain ?? string.Empty;
                    return new CookieDto
                    {
                        Name = c.Name,
                        Value = c.Value,
                        Domain = domain,
                        Path = string.IsNullOrEmpty(c.Path) ? "/" : c.Path,
                        Secure = c.Secure ?? false
                    };
                })
                .ToArray();
        }

        private static string? DetectPlatform(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return null;

            if (address.Contains("douyin.com", StringComparison.OrdinalIgnoreCase)) return "douyin";
            if (address.Contains("bilibili.com", StringComparison.OrdinalIgnoreCase)) return "bilibili";
            if (address.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
                address.Contains("youtu.be", StringComparison.OrdinalIgnoreCase)) return "youtube";
            if (address.Contains("xiaohongshu.com", StringComparison.OrdinalIgnoreCase)) return "xiaohongshu";

            return null;
        }

        private static string? ExtractVideoId(string url, string platform)
        {
            if (platform == "douyin")
            {
                var matchModal = Regex.Match(url, @"modal_id=(\d+)");
                if (matchModal.Success) return matchModal.Groups[1].Value;

                var matchVideo = Regex.Match(url, @"/video/(\d+)");
                if (matchVideo.Success) return matchVideo.Groups[1].Value;
            }
            else if (platform == "bilibili")
            {
                var matchBV = Regex.Match(url, @"(BV[a-zA-Z0-9]+)");
                if (matchBV.Success) return matchBV.Groups[1].Value;
            }
            else if (platform == "youtube")
            {
                var matchV = Regex.Match(url, @"(?:v=|youtu\.be/|/shorts/)([\w-]+)");
                if (matchV.Success) return matchV.Groups[1].Value;
            }
            else if (platform == "xiaohongshu")
            {
                var matchExplore = Regex.Match(url, @"explore/([\w-]+)");
                if (matchExplore.Success) return matchExplore.Groups[1].Value;
            }

            return null;
        }
    }

    public sealed class BrowserVideoRequest
    {
        public BrowserVideoRequest(string platform, string videoId, string address)
        {
            Platform = platform;
            VideoId = videoId;
            Address = address;
        }

        public string Platform { get; }
        public string VideoId { get; }
        public string Address { get; }
    }
}
