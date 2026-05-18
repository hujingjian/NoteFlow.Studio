using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;
using NoteFlow.Studio.Service.Interfaces;
using NoteFlow.Studio.Service.Models;
using NoteFlow.Studio.Infrastructure.Logging;

namespace NoteFlow.Studio.Service.VideoParsers
{
    public class BilibiliVideoParserService : IVideoParserService
    {
        private readonly Providers.IPlaywrightProvider _playwrightProvider;

        public BilibiliVideoParserService(Providers.IPlaywrightProvider playwrightProvider)
        {
            _playwrightProvider = playwrightProvider;
        }

        public string PlatformName => "Bilibili";
        public string Platform => "B站";

        public async Task<VideoInfo?> ExtractVideoDetailAsync(string videoId, IEnumerable<CookieDto> cookies, string address = "")
        {
            var videoInfo = new VideoInfo { Platform = Platform, VideoId = videoId };
            bool updated = false;

            var browser = await _playwrightProvider.GetBrowserAsync();

            // 创建浏览器上下文
            var contextOptions = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                UserAgent = "Mozilla/5.0 (iPhone; CPU iPhone OS 16_6 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/16.6 Mobile/15E148 Safari/604.1",
                ViewportSize = new ViewportSize { Width = 375, Height = 812 },
                DeviceScaleFactor = 3,
                IsMobile = true,
                HasTouch = true,
                Locale = "zh-CN",
                TimezoneId = "Asia/Shanghai",
                ExtraHTTPHeaders = new Dictionary<string, string>
                {
                    ["referer"] = "https://m.bilibili.com/",
                    ["accept"] = "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8",
                    ["accept-encoding"] = "gzip, deflate, br",
                    ["accept-language"] = "zh-CN,zh;q=0.9,en-US;q=0.8,en;q=0.7",
                    ["sec-fetch-dest"] = "document",
                    ["sec-fetch-mode"] = "navigate",
                    ["sec-fetch-site"] = "none",
                    ["sec-fetch-user"] = "?1",
                    ["upgrade-insecure-requests"] = "1"
                }
            });

            if (cookies != null && cookies.Any())
            {
                var playwrightCookies = cookies.Select(c => new Microsoft.Playwright.Cookie 
                { 
                    Name = c.Name, 
                    Value = c.Value, 
                    Domain = ".bilibili.com", 
                    Path = "/",
                    Secure = false, // 允许HTTP和HTTPS
                    HttpOnly = false,
                    SameSite = SameSiteAttribute.Lax // 更宽松的同站策略
                }).ToList();
                await contextOptions.AddCookiesAsync(playwrightCookies);
            }

            await contextOptions.AddInitScriptAsync(@"
                    Object.defineProperty(navigator, 'webdriver', {
                        get: () => undefined,
                    });
                    Object.defineProperty(navigator, 'plugins', {
                        get: () => [1, 2, 3, 4, 5],
                    });
                    Object.defineProperty(navigator, 'languages', {
                        get: () => ['zh-CN', 'zh', 'en-US', 'en'],
                    });
                    Object.defineProperty(navigator, 'platform', {
                        get: () => 'iPhone',
                    });
                    Object.defineProperty(navigator, 'maxTouchPoints', {
                        get: () => 5,
                    });
                    delete window.__playwright__;
                    delete window.__pw_chromium__;
                    Intl.DateTimeFormat.prototype.resolvedOptions = function() {
                        return { timeZone: 'Asia/Shanghai', locale: 'zh-CN' };
                    }
                ");

            var page = await contextOptions.NewPageAsync();
            // 用于存储拦截到的 API 数据
            string? playUrlApiResponse = null;
            string? viewApiResponse = null;

            page.RequestFinished += async (sender, request) =>
            {
                try
                {
                    // 拦截播放URL API
                    if (request.Url.Contains("/x/player/wbi/playurl"))
                    {
                        var response = await request.ResponseAsync();
                        if (response != null && response.Status == 200)
                        {
                            playUrlApiResponse = await response.TextAsync();
                        }
                        else
                        {
                        }
                    }

                    // 拦截视频详情 API
                    if (request.Url.Contains("/x/web-interface/view"))
                    {
                        var response = await request.ResponseAsync();
                        if (response != null && response.Status == 200)
                        {
                            viewApiResponse = await response.TextAsync();
                        }
                        else
                        {
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"B站解析异常: {ex.Message}", ex);
                }
            };

            try
            {
                //string targetUrl = $"https://player.bilibili.com/player.html?bvid={videoId}&page=1";
                string targetUrl = $"https://www.bilibili.com/blackboard/webplayer/mbplayer.html?bvid={videoId}&page=1";
                await page.GotoAsync(targetUrl, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.Commit,
                    Timeout = 30000
                }); 
                var inhtml= await page.ContentAsync();
                var maxWaitTime = TimeSpan.FromSeconds(30);
                var startTime = DateTime.Now;
                //拦截30秒超时
                while ((string.IsNullOrEmpty(viewApiResponse) || string.IsNullOrEmpty(playUrlApiResponse)) && DateTime.Now - startTime < maxWaitTime)
                {
                    await Task.Delay(500); // 每500ms检查一次
                }
                // 解析拦截到的API数据
                if (!string.IsNullOrEmpty(viewApiResponse))
                {
                    ParseViewApiResponse(viewApiResponse, videoInfo);
                    updated = true;
                }
                else
                {
                }

                if (!string.IsNullOrEmpty(playUrlApiResponse))
                {
                    ParsePlayUrlApiResponse(playUrlApiResponse, videoInfo);
                    updated = true;
                }
                else
                {
                }
                videoInfo.PageUrl = $"https://www.bilibili.com/video/{videoId}";
            }
            catch (Exception ex)
            {
                AppLogger.Error($"B站解析异常: {ex.Message}", ex);
            }
            finally
            {
                await page.CloseAsync();
                await contextOptions.CloseAsync();
            }

            return updated ? videoInfo : null;
        }

        public List<VideoInfo> ParseVideoInfo(string url, string jsonContent)
        {
            return new List<VideoInfo>(); // 默认目前不需要解析列表，或留空
        }

        public VideoInfo? ParseYtDlpJson(string json)
        {
            try
            {
                var doc = JsonDocument.Parse(json).RootElement;
                return new VideoInfo
                {
                    Platform = PlatformName,
                    VideoId = doc.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                    Title = doc.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
                    Description = doc.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "",
                    Author = doc.TryGetProperty("uploader", out var u) ? u.GetString() ?? "" : "",
                    PlayCount = doc.TryGetProperty("view_count", out var vc) && vc.ValueKind == JsonValueKind.Number ? vc.GetInt64() : 0,
                    DiggCount = doc.TryGetProperty("like_count", out var lc) && lc.ValueKind == JsonValueKind.Number ? lc.GetInt64() : 0,
                };
            }
            catch
            {
                return null;
            }
        }
        /// <summary>
        /// 解析视频详情API响应 (/x/web-interface/view)
        /// </summary>
        private void ParseViewApiResponse(string jsonResponse, VideoInfo videoInfo)
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonResponse);
                var root = doc.RootElement;

                // 检查响应码
                if (root.TryGetProperty("code", out var code) && code.GetInt32() != 0)
                {
                    return;
                }

                if (!root.TryGetProperty("data", out var data))
                {
                    return;
                }

                // 提取 bvid
                if (data.TryGetProperty("bvid", out var bvidElem))
                {
                    videoInfo.VideoId = bvidElem.GetString() ?? videoInfo.VideoId;
                }

                // 提取标题
                if (data.TryGetProperty("title", out var title))
                {
                    videoInfo.Title = title.GetString() ?? "";
                }

                // 提取描述
                if (data.TryGetProperty("desc", out var desc))
                {
                    videoInfo.Description = desc.GetString() ?? "";
                }

                // 提取封面
                if (data.TryGetProperty("pic", out var pic))
                {
                    videoInfo.CoverUrl = NormalizeBilibiliPicUrl(pic.GetString()) ?? "";
                }

                // 提取时长（秒）
                if (data.TryGetProperty("duration", out var duration))
                {
                    videoInfo.Duration = duration.GetInt32();
                }

                // 提取作者信息
                if (data.TryGetProperty("owner", out var owner))
                {
                    if (owner.TryGetProperty("name", out var name))
                    {
                        videoInfo.Author = name.GetString() ?? "";
                    }
                }

                // 提取统计信息
                if (data.TryGetProperty("stat", out var stat))
                {
                    if (stat.TryGetProperty("view", out var view))
                    {
                        videoInfo.PlayCount = view.GetInt64();
                    }
                    if (stat.TryGetProperty("like", out var like))
                    {
                        videoInfo.DiggCount = like.GetInt64();
                    }
                    if (stat.TryGetProperty("favorite", out var favorite))
                    {
                        videoInfo.CollectCount = favorite.GetInt64();
                    }
                    if (stat.TryGetProperty("reply", out var reply))
                    {
                        videoInfo.CommentCount = reply.GetInt64();
                    }
                    if (stat.TryGetProperty("share", out var share))
                    {
                        videoInfo.ShareCount = share.GetInt64();
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error($"B站解析异常: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 解析播放URL API响应 (/x/player/wbi/playurl)
        /// </summary>
        private void ParsePlayUrlApiResponse(string jsonResponse, VideoInfo videoInfo)
        {
            try
            {
                // 解码 Unicode 转义字符
                jsonResponse = Regex.Replace(jsonResponse, @"\\u([0-9A-Fa-f]{4})", match =>
                {
                    var code = Convert.ToInt32(match.Groups[1].Value, 16);
                    return ((char)code).ToString();
                });

                using var doc = JsonDocument.Parse(jsonResponse);
                var root = doc.RootElement;

                // 检查响应码
                if (root.TryGetProperty("code", out var code) && code.GetInt32() != 0)
                {
                    return;
                }

                if (!root.TryGetProperty("data", out var data))
                {
                    return;
                }

                // 提取视频URL（从 durl 数组）
                if (data.TryGetProperty("durl", out var durl) && durl.GetArrayLength() > 0)
                {
                    var firstDurl = durl[0];

                    // 获取视频URL
                    if (firstDurl.TryGetProperty("url", out var url))
                    {
                        var videoUrl = url.GetString();
                        // 标准化B站视频URL域名（统一替换为 upos-sz-mirrorhw）
                        videoInfo.VideoUrl = NormalizeBilibiliVideoUrl(videoUrl) ?? "";
                    }
                    if (firstDurl.TryGetProperty("size", out var size))
                    {
                        videoInfo.Size = size.GetInt64();
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error($"B站解析异常: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 标准化B站视频URL域名
        /// 将各种 upos-sz-mirror* 域名统一替换为 upos-sz-mirrorhw（方便微信小程序配置）
        /// 同时将 http 替换为 https
        /// </summary>
        private string? NormalizeBilibiliVideoUrl(string? url)
        {
            if (string.IsNullOrEmpty(url))
                return url;

            // 先将 http 替换为 https
            url = url.Replace("http://", "https://", StringComparison.OrdinalIgnoreCase);

            // 统一域名格式：将所有 upos-sz-mirror{任意字符}.bilivideo.com 替换为 upos-sz-mirrorhw.bilivideo.com
            // 匹配模式：upos-sz-mirror + 任意字符（如 08, 08h, cos, bda2 等）
            var pattern = @"https://.*\.bilivideo\.com";
            url = Regex.Replace(url, pattern, "https://upos-sz-mirrorhw.bilivideo.com", RegexOptions.IgnoreCase);

            return url;
        }
        /// <summary>
        /// 标准化B站图片URL域名
        /// 将各种 i*.hdslb.com 域名统一替换为 https://i0.hdslb.com（方便微信小程序配置）
        /// 同时将 http 替换为 https
        /// </summary>
        private string? NormalizeBilibiliPicUrl(string? url)
        {
            if (string.IsNullOrEmpty(url))
                return url;

            // 先将 http 替换为 https
            url = url.Replace("http://", "https://", StringComparison.OrdinalIgnoreCase);

            // 统一域名格式：将所有 {任意字符}.hdslb.com 替换为 i0.hdslb.com
            // 匹配模式：i*.hdslb.com + 任意字符（如 08, 08h, cos, bda2 等）
            var pattern = @"https://.*\.hdslb\.com";
            url = Regex.Replace(url, pattern, "https://i0.hdslb.com", RegexOptions.IgnoreCase);

            return url;
        }

    }
}

/// <summary>
/// B站视频详情模型
/// </summary>
public class BilibiliVideoDetail
{
    public string? Bvid { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? AuthorName { get; set; }
    public string? AuthorId { get; set; }
    /// <summary>
    /// 视频URL（手机端为完整MP4，包含音视频）
    /// </summary>
    public string? VideoUrl { get; set; }
    /// <summary>
    /// 音频URL（PC端分离的音频流，手机端不需要）
    /// </summary>
    public string? AudioUrl { get; set; }
    public string? CoverUrl { get; set; }
    public TimeSpan Duration { get; set; }
    public DateTime CreateTime { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public long? ViewCount { get; set; }
    public long? LikeCount { get; set; }
    public long? CommentCount { get; set; }
    public long? ShareCount { get; set; }
    /// <summary>
    /// 视频文件大小（字节）
    /// </summary>
    public long? VideoSize { get; set; }
    /// <summary>
    /// 备用视频URL列表
    /// </summary>
    public List<string>? BackupVideoUrls { get; set; }
}
