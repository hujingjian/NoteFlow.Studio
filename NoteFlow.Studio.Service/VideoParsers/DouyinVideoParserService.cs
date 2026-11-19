using Microsoft.Playwright;
using Newtonsoft.Json;
using NoteFlow.Studio.Service.Interfaces;
using NoteFlow.Studio.Service.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NoteFlow.Studio.Infrastructure.Logging;

namespace NoteFlow.Studio.Service.VideoParsers;

public class DouyinVideoParserService : IVideoParserService
{
    private readonly Providers.IPlaywrightProvider _playwrightProvider;

    public DouyinVideoParserService(Providers.IPlaywrightProvider playwrightProvider)
    {
        _playwrightProvider = playwrightProvider;
    }

    public string PlatformName => "Douyin";
    public string Platform => "抖音";

    public async Task<VideoInfo?> ExtractVideoDetailAsync(string videoId, IEnumerable<CookieDto> cookies, string address = "")
    {
        var videoInfo = new VideoInfo { Platform = Platform, VideoId = videoId };
        var browser = await _playwrightProvider.GetBrowserAsync();

        // 创建浏览器上下文（使用完整的请求头配置）
        var contextOptions = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
            ViewportSize = new ViewportSize { Width = 1920, Height = 1080 },
            Locale = "zh-CN",
            TimezoneId = "Asia/Shanghai",
            ExtraHTTPHeaders = new Dictionary<string, string>
            {
                ["Accept"] = "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8",
                ["Accept-Language"] = "zh-CN,zh;q=0.9,en;q=0.8",
                ["Accept-Encoding"] = "gzip, deflate, br",
                ["DNT"] = "1",
                ["Connection"] = "keep-alive",
                ["Upgrade-Insecure-Requests"] = "1",
                ["Sec-Ch-Ua"] = "\"Not_A Brand\";v=\"8\", \"Chromium\";v=\"120\", \"Google Chrome\";v=\"120\"",
                ["Sec-Ch-Ua-Mobile"] = "?0",
                // 关键：强制设置为 Windows 平台
                ["Sec-Ch-Ua-Platform"] = "\"Windows\"",
                ["Sec-Fetch-Dest"] = "document",
                ["Sec-Fetch-Mode"] = "navigate",
                ["Sec-Fetch-Site"] = "none",//
                ["Upgrade-Insecure-Requests"] = "1"
            }
        });
        await contextOptions.AddInitScriptAsync(@"
            // 覆盖 navigator 属性，伪装成 Windows 平台
            Object.defineProperty(navigator, 'webdriver', {
                get: () => undefined,
            });
            Object.defineProperty(navigator, 'plugins', {
                get: () => [1, 2, 3, 4, 5],
            });
            Object.defineProperty(navigator, 'languages', {
                get: () => ['zh-CN', 'zh', 'en'],
            });
            Object.defineProperty(navigator, 'platform', {
                get: () => 'Win32',
            });
            window.chrome = { runtime: {} };
            // 新增：移除自动化特征
            delete window.__playwright__;
            delete window.__pw_chromium__;
            // 新增：伪装时区
            Intl.DateTimeFormat.prototype.resolvedOptions = function() {
                return { timeZone: 'Asia/Shanghai', locale: 'zh-CN' };
            }
        ");

        if (cookies != null && cookies.Any())
        {
            var playwrightCookies = cookies.Select(c => new Microsoft.Playwright.Cookie
            {
                Name = c.Name,
                Value = c.Value,
                Domain = ".douyin.com", // 使用 .domain.com 格式自动匹配所有子域名
                Path = "/",
                Secure = false, // 允许HTTP和HTTPS
                HttpOnly = false,
                SameSite = SameSiteAttribute.Lax // 更宽松的同站策略
            }).ToList();
            await contextOptions.AddCookiesAsync(playwrightCookies);
        }

        var page = await contextOptions.NewPageAsync();

        page.RequestFinished += async (sender, request) =>
        {
            var url = request.Url;
            if (url.Contains("aweme/v1/web/aweme/detail/"))
            {
                try
                {
                    if (request.Url.Contains("/aweme/v1/web/aweme/detail/"))
                    {
                        var response = await request.ResponseAsync();
                        if (request.Failure == null && response != null)
                        {
                            if (response.Status == 200)
                            {
                                string responseBody = await response.TextAsync();
                                videoInfo = ParseVideoInfo(request.Url, responseBody).FirstOrDefault();
                            }
                            else
                            {
                                AppLogger.Warn($"抖音API解析失败: {response.Status}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"抖音解析异常: {ex.Message}", ex);
                }
            }
        };

        try
        {
            string targetUrl = "https://www.douyin.com/video/" + videoId;
            await page.GotoAsync(targetUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.Commit,
                Timeout = 30000
            }); 
            var maxWaitTime = TimeSpan.FromSeconds(30);
            var startTime = DateTime.Now;
            bool captchaHandled = false;
            while (string.IsNullOrEmpty(videoInfo.Title) && DateTime.Now - startTime < maxWaitTime)
            {
                var finalHtml = await page.ContentAsync();
                // 检测验证码中间页
                if (!captchaHandled && IsCaptchaPage(finalHtml))
                {
                    AppLogger.Warn("检测到抖音验证码页面，正在切换为有头浏览器进行手动验证...");
                    captchaHandled = true;

                    // 获取当前 cookies
                    var currentCookies = await contextOptions.CookiesAsync();

                    // 关闭当前无头上下文和页面
                    await page.CloseAsync();
                    await contextOptions.CloseAsync();

                    // 启动有头浏览器
                    var headedBrowser = await _playwrightProvider.GetHeadedBrowserAsync();
                    var headedContext = await headedBrowser.NewContextAsync(new BrowserNewContextOptions
                    {
                        UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
                        ViewportSize = new ViewportSize { Width = 1920, Height = 1080 },
                        Locale = "zh-CN",
                        TimezoneId = "Asia/Shanghai"
                    });
                    // 迁移 cookies
                    if (currentCookies != null && currentCookies.Count > 0)
                    {
                        var mappedCookies = currentCookies.Select(c => new Microsoft.Playwright.Cookie
                        {
                            Name = c.Name,
                            Value = c.Value,
                            Domain = c.Domain,
                            Path = c.Path,
                            Expires = c.Expires,
                            HttpOnly = c.HttpOnly,
                            Secure = c.Secure,
                            SameSite = c.SameSite
                        }).ToList();
                        await headedContext.AddCookiesAsync(mappedCookies);
                    }

                    var headedPage = await headedContext.NewPageAsync();

                    // 重新监听 API 响应
                    headedPage.RequestFinished += async (sender, request) =>
                    {
                        var url = request.Url;
                        if (url.Contains("aweme/v1/web/aweme/detail/"))
                        {
                            try
                            {
                                var response = await request.ResponseAsync();
                                if (request.Failure == null && response != null && response.Status == 200)
                                {
                                    string responseBody = await response.TextAsync();
                                    videoInfo = ParseVideoInfo(request.Url, responseBody).FirstOrDefault();
                                }
                            }
                            catch (Exception ex)
                            {
                                AppLogger.Error($"抖音有头浏览器解析异常: {ex.Message}", ex);
                            }
                        }
                    };

                    await headedPage.GotoAsync(targetUrl, new PageGotoOptions
                    {
                        WaitUntil = WaitUntilState.Commit,
                        Timeout = 30000
                    });

                    // 等待用户手动完成验证，最多等待 3 分钟
                    AppLogger.Warn("请在弹出的浏览器窗口中完成验证码，程序将在验证通过后自动继续...");
                    var captchaWaitStart = DateTime.Now;
                    var captchaMaxWait = TimeSpan.FromMinutes(3);
                    while (string.IsNullOrEmpty(videoInfo.Title) && DateTime.Now - captchaWaitStart < captchaMaxWait)
                    {
                        var headedHtml = await headedPage.ContentAsync();
                        if (!IsCaptchaPage(headedHtml))
                        {
                            AppLogger.Info("验证码已通过，等待数据加载...");
                        }
                        await Task.Delay(1000);
                    }

                    videoInfo.PageUrl = targetUrl;

                    await headedPage.CloseAsync();
                    await headedContext.CloseAsync();
                    await headedBrowser.CloseAsync();

                    // 跳出外层 while
                    break;
                }
                await Task.Delay(500); // 每500ms检查一次
            }
            videoInfo.PageUrl = targetUrl;
        }
        catch(Exception ex)
        {
            AppLogger.Error($"抖音解析异常: {ex.Message}", ex);
        }
        finally
        {
            try { await page.CloseAsync(); } catch { }
            try { await contextOptions.CloseAsync(); } catch { }
        }

        return videoInfo;
    }

    private static bool IsCaptchaPage(string html)
    {
        if (string.IsNullOrEmpty(html)) return false;
        return html.Contains("验证码中间页", StringComparison.OrdinalIgnoreCase)
            || html.Contains("滑块验证", StringComparison.OrdinalIgnoreCase);
    }

    public List<VideoInfo> ParseVideoInfo(string url, string json)
    {
        var videoList = new List<VideoInfo>();
        try
        {
            var jsonNode = JsonNode.Parse(json);
            if (jsonNode != null)
            {
                var awemeDetail = jsonNode["aweme_detail"];
                if (awemeDetail != null)
                {
                    var videoInfo = new VideoInfo
                    {
                        Platform = Platform,
                        Title = awemeDetail["desc"]?.ToString() ?? "",
                        Description = awemeDetail["video"]?["caption"]?.ToString() ?? "",
                        CoverUrl = awemeDetail["video"]?["cover"]?["url_list"]?.AsArray()?.FirstOrDefault()?.ToString() ?? "",
                        Author = awemeDetail["author"]?["nickname"]?.ToString() ?? "",
                        VideoId = awemeDetail["aweme_id"]?.ToString() ?? "",
                        PlayCount = awemeDetail["statistics"]?["play_count"]?.GetValue<long>() ?? 0,
                        DiggCount = awemeDetail["statistics"]?["digg_count"]?.GetValue<long>() ?? 0,
                        CollectCount = awemeDetail["statistics"]?["collect_count"]?.GetValue<long>() ?? 0,
                        VideoUrl = ConvertToColdUrl(awemeDetail["video"]?["play_addr"]?["url_list"]?.AsArray()?.FirstOrDefault()?.ToString() ?? ""),
                        Duration = awemeDetail["video"]?["duration"]?.GetValue<long>() ?? 0,
                        Size = awemeDetail["video"]?["play_addr"]?["data_size"]?.GetValue<long>() ?? 0,
                        VideoStreams = new List<VideoStream>()
                    };
                    videoList.Add(videoInfo);
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error($"抖音解析异常: {ex.Message}", ex);
        }
        return videoList;
    }


    /// <summary>
    /// 将抖音视频URL的web域名替换为cold域名（去权限验证）
    /// </summary>
    public string? ConvertToColdUrl(string? url)
    {
        if (string.IsNullOrEmpty(url))
            return url;

        // 先解码 \u0026 等转义
        url = Regex.Unescape(url);

        // 处理常见明确的 web -> cold 替换（覆盖常见子域）
        url = url.Replace("v26-web.douyinvod.com", "v26-cold.douyinvod.com", StringComparison.OrdinalIgnoreCase);
        url = url.Replace("v3-web.douyinvod.com", "v3-cold.douyinvod.com", StringComparison.OrdinalIgnoreCase);
        url = url.Replace("v6-web.douyinvod.com", "v6-cold.douyinvod.com", StringComparison.OrdinalIgnoreCase);
        url = url.Replace("v9-web.douyinvod.com", "v9-cold.douyinvod.com", StringComparison.OrdinalIgnoreCase);
        url = url.Replace("v11-web.douyinvod.com", "v11-cold.douyinvod.com", StringComparison.OrdinalIgnoreCase);

        // 通用替换：匹配 v{number}-*web* .douyinvod.com 的各种变体（例如 v26-web, v26-webf, v26-web-xxx 等）
        var pattern = @"v(\d+)-[a-z0-9\-]*web[a-z0-9\-]*\.douyinvod\.com";
        url = Regex.Replace(url, pattern, "v$1-cold.douyinvod.com", RegexOptions.IgnoreCase);

        // 另外处理可能存在的带端口或额外路径的情况，确保主机部分被替换
        // 如果还有某些未覆盖的 web 子域（极少数），再做一次宽松替换，确保不把非douyinvod域替换
        // 宽松匹配仅针对包含 douyinvod 的主机部分
        var hostPattern = @"(v(\d+)-[^./\\\s]*?)\.douyinvod\.com";
        url = Regex.Replace(url, hostPattern, m =>
        {
            var hostPrefix = m.Groups[1].Value; // like v26-webf
            var numMatch = Regex.Match(hostPrefix, @"v(\d+)");
            if (numMatch.Success)
            {
                return $"v{numMatch.Groups[1].Value}-cold.douyinvod.com";
            }
            return m.Value;
        }, RegexOptions.IgnoreCase);

        return url;
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
}
