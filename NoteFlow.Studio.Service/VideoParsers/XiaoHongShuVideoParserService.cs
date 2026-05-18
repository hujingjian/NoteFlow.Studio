using Microsoft.Playwright;
using NoteFlow.Studio.Service.Interfaces;
using NoteFlow.Studio.Service.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NoteFlow.Studio.Infrastructure.Logging;

namespace NoteFlow.Studio.Service.VideoParsers
{
    public class XiaoHongShuVideoParserService : IVideoParserService
    {
        private readonly Providers.IPlaywrightProvider _playwrightProvider;

        public XiaoHongShuVideoParserService(Providers.IPlaywrightProvider playwrightProvider)
        {
            _playwrightProvider = playwrightProvider;
        }

        public string PlatformName => "XiaoHongShu";
        public string Platform => "小红书";

        public async Task<VideoInfo?> ExtractVideoDetailAsync(string videoId, IEnumerable<CookieDto> cookies, string address = "")
        {
            VideoInfo? videoInfo = null;

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
                    ["accept"] = "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8",
                    ["accept-encoding"] = "gzip, deflate, br",
                    ["accept-language"] = "zh-CN,zh;q=0.9",
                    ["sec-ch-ua"] = "\"Not_A Brand\";v=\"8\", \"Chromium\";v=\"120\"",
                    ["sec-ch-ua-mobile"] = "?0",
                    ["sec-ch-ua-platform"] = "\"Windows\"",
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
                    Domain = ".xiaohongshu.com", // 使用 .domain.com 格式自动匹配所有子域名
                    Path = "/",
                    Secure = false, // 允许HTTP和HTTPS
                    HttpOnly = false,
                    SameSite = SameSiteAttribute.Lax // 更宽松的同站策略
                }).ToList();
                await contextOptions.AddCookiesAsync(playwrightCookies);
            }
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

            var page = await contextOptions.NewPageAsync();

            try
            {
                XiaoHongShuVideoDetail? videoDetail = null;
                string targetUrl = address;//$"https://www.xiaohongshu.com/explore/{videoId}";
                await page.GotoAsync(targetUrl, new PageGotoOptions
                {
                    Timeout = 35000,  // 35秒超时
                    WaitUntil = WaitUntilState.DOMContentLoaded  //等待网络空闲+所有JS执行完成+DOM渲染完毕 // DOMContentLoaded 只等DOM加载，不等资源
                });
                var middleHtml = await page.ContentAsync();
                await page.WaitForSelectorAsync("div[id='noteContainer']", new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible, Timeout = 30000 });
                var finalHtml = await page.ContentAsync();
                // 拿到的是浏览器F12看到的完整HTML                                      
                // 尝试从HTML中提取JSON数据
                videoDetail = ExtractDetailFromHtml(finalHtml, videoId);
                if (videoDetail != null)
                {
                    videoInfo = new VideoInfo
                    {
                        Platform = Platform,
                        VideoId = videoId,
                        Title = videoDetail.Title ?? string.Empty,
                        Description = videoDetail.Description ?? string.Empty,
                        Author = videoDetail.AuthorName ?? string.Empty,
                        CoverUrl = videoDetail.CoverUrl ?? string.Empty,
                        DiggCount = videoDetail.CollectCount ?? videoDetail.LikeCount ?? 0,
                        VideoUrl = videoDetail.VideoUrl ?? string.Empty,
                        Duration = (long)(videoDetail.Duration.TotalSeconds),
                        Size = videoDetail.VideoSize ?? 0,
                    };
                }
            }
            catch { }
            finally
            {
                await page.CloseAsync();
                await contextOptions.CloseAsync();
            }

            return videoInfo;
        }

        public List<VideoInfo> ParseVideoInfo(string url, string jsonContent)
        {
            return new List<VideoInfo>();
        }

        public VideoInfo? ParseYtDlpJson(string jsonContent)
        {
            return null;
        }
        /// <summary>
        /// 从HTML内容中提取视频详情
        /// </summary>
        private XiaoHongShuVideoDetail? ExtractDetailFromHtml(string html, string noteId)
        {
            try
            {
                // 使用<script>标签限定范围，提取完整的 window.__INITIAL_STATE__
                var scriptPattern = @"<script>\s*window\.__INITIAL_STATE__\s*=\s*(\{.+?\})\s*</script>";
                var scriptMatch = Regex.Match(html, scriptPattern, RegexOptions.Singleline);

                if (scriptMatch.Success)
                {
                    var jsonData = scriptMatch.Groups[1].Value;

                    // 解析JSON数据
                    var detail = ParseInitialStateJson(jsonData, noteId);
                    if (detail != null)
                    {
                        return detail;
                    }
                }
                else
                {
                    // 备用方案：不使用script标签限定，使用手动括号计数
                    var startPattern = @"window\.__INITIAL_STATE__\s*=\s*";
                    var startMatch = Regex.Match(html, startPattern);

                    if (startMatch.Success)
                    {
                        var startIndex = startMatch.Index + startMatch.Length;
                        var jsonData = ExtractJsonFromPosition(html, startIndex);

                        if (!string.IsNullOrEmpty(jsonData))
                        {
                            var detail = ParseInitialStateJson(jsonData, noteId);
                            if (detail != null)
                            {
                                return detail;
                            }
                        }
                    }
                }
                return null;
            }
            catch (Exception ex)
            {
                AppLogger.Error("An error occurred while finding initial state JSON.", ex);
                return null;
            }
        }

        /// <summary>
        /// 从指定位置提取完整的JSON对象（通过括号计数）
        /// </summary>
        private string? ExtractJsonFromPosition(string html, int startIndex)
        {
            try
            {
                if (startIndex >= html.Length || html[startIndex] != '{')
                {
                    return null;
                }

                int braceCount = 0;
                bool inString = false;
                bool isEscaped = false;
                int endIndex = startIndex;

                for (int i = startIndex; i < html.Length; i++)
                {
                    char c = html[i];

                    // 处理字符串内的内容
                    if (inString)
                    {
                        if (isEscaped)
                        {
                            isEscaped = false;
                        }
                        else if (c == '\\')
                        {
                            isEscaped = true;
                        }
                        else if (c == '"')
                        {
                            inString = false;
                        }
                    }
                    else
                    {
                        // 处理字符串开始
                        if (c == '"')
                        {
                            inString = true;
                        }
                        // 计数大括号
                        else if (c == '{')
                        {
                            braceCount++;
                        }
                        else if (c == '}')
                        {
                            braceCount--;

                            // 找到完整的JSON对象
                            if (braceCount == 0)
                            {
                                endIndex = i + 1;
                                break;
                            }
                        }
                    }
                }

                if (braceCount == 0 && endIndex > startIndex)
                {
                    var jsonLength = endIndex - startIndex;
                    var jsonData = html.Substring(startIndex, jsonLength);
                    return jsonData;
                }
                else
                {
                    return null;
                }
            }
            catch (Exception ex)
            {AppLogger.Error("An error occurred while extracting JSON.", ex);
                
                return null;
            }
        }

        /// <summary>
        /// 解析 __INITIAL_STATE__ JSON
        /// </summary>
        private XiaoHongShuVideoDetail? ParseInitialStateJson(string jsonData, string noteId)
        {
            try
            {
                jsonData = jsonData.Replace("undefined", "\"\"");
                // 解码 Unicode 转义字符 (\u002F -> /)
                jsonData = Regex.Replace(jsonData, @"\\u([0-9A-Fa-f]{4})", match =>
                {
                    var code = Convert.ToInt32(match.Groups[1].Value, 16);
                    return ((char)code).ToString();
                });

                using var doc = JsonDocument.Parse(jsonData);
                var root = doc.RootElement;

                var detail = new XiaoHongShuVideoDetail { NoteId = noteId };

                // 导航到 note.noteDetailMap[noteId].note
                if (root.TryGetProperty("note", out var noteElement) &&
                    noteElement.TryGetProperty("noteDetailMap", out var noteDetailMap) &&
                    noteDetailMap.TryGetProperty(noteId, out var noteDetail) &&
                    noteDetail.TryGetProperty("note", out var noteInfo))
                {
                    // 提取内容类型 (type: "normal"=图文, "video"=视频)
                    if (noteInfo.TryGetProperty("type", out var typeElement))
                    {
                        detail.Type = typeElement.GetString();
                    }

                    // 提取标题
                    if (noteInfo.TryGetProperty("title", out var title))
                    {
                        detail.Title = title.GetString();
                    }

                    // 提取描述
                    if (noteInfo.TryGetProperty("desc", out var desc))
                    {
                        detail.Description = desc.GetString();
                    }

                    // 提取创建时间 (time) - 毫秒级时间戳
                    if (noteInfo.TryGetProperty("time", out var time))
                    {
                        var timeMs = time.GetInt64();
                        detail.CreateTime = DateTimeOffset.FromUnixTimeMilliseconds(timeMs).DateTime;
                    }

                    // 提取作者信息
                    if (noteInfo.TryGetProperty("user", out var user))
                    {
                        if (user.TryGetProperty("nickname", out var nickname))
                        {
                            detail.AuthorName = nickname.GetString();
                        }
                        if (user.TryGetProperty("userId", out var userId))
                        {
                            detail.AuthorId = userId.GetString();
                        }
                    }

                    // 提取互动数据
                    if (noteInfo.TryGetProperty("interactInfo", out var interactInfo))
                    {
                        if (interactInfo.TryGetProperty("likedCount", out var likedCount))
                        {
                            var likeText = likedCount.GetString();
                            detail.LikeCount = ParseCountString(likeText);
                        }
                        if (interactInfo.TryGetProperty("collectedCount", out var collectedCount))
                        {
                            var collectText = collectedCount.GetString();
                            detail.CollectCount = ParseCountString(collectText);
                        }
                        if (interactInfo.TryGetProperty("commentCount", out var commentCount))
                        {
                            var commentText = commentCount.GetString();
                            detail.CommentCount = ParseCountString(commentText);
                        }
                        if (interactInfo.TryGetProperty("shareCount", out var shareCount))
                        {
                            var shareText = shareCount.GetString();
                            detail.ShareCount = ParseCountString(shareText);
                        }
                    }

                    // 提取图片列表（图文和视频都可能有imageList）
                    if (noteInfo.TryGetProperty("imageList", out var imageList) &&
                        imageList.GetArrayLength() > 0)
                    {
                        detail.Images = new List<XhsImageItem>();

                        for (int i = 0; i < imageList.GetArrayLength(); i++)
                        {
                            var img = imageList[i];
                            var imageItem = new XhsImageItem
                            {
                                Width = img.TryGetProperty("width", out var w) ? w.GetInt32() : 0,
                                Height = img.TryGetProperty("height", out var h) ? h.GetInt32() : 0,
                                UrlDefault = img.TryGetProperty("urlDefault", out var urlDefault) ? urlDefault.GetString() : null,
                                UrlPre = img.TryGetProperty("urlPre", out var urlPre) ? urlPre.GetString() : null,
                                InfoList = new List<string>()
                            };

                            // 提取infoList中的所有URL
                            if (img.TryGetProperty("infoList", out var infoList) && infoList.GetArrayLength() > 0)
                            {
                                var urlList = new List<string>();
                                for (int j = 0; j < infoList.GetArrayLength(); j++)
                                {
                                    var info = infoList[j];
                                    if (info.TryGetProperty("url", out var url))
                                    {
                                        var urlStr = url.GetString();
                                        if (!string.IsNullOrEmpty(urlStr))
                                        {
                                            urlList.Add(urlStr);
                                        }
                                    }
                                }
                                imageItem.InfoList = urlList; // 使用setter自动转换并替换域名
                            }

                            detail.Images.Add(imageItem);
                        }

                        // 第一张图片作为封面（自动转换并替换域名）
                        if (detail.Images.Count > 0 && !string.IsNullOrEmpty(detail.Images[0].UrlDefault))
                        {
                            detail.CoverUrl = NormalizeXhsPicUrl(detail.Images[0].UrlDefault);
                        }
                    }

                    // 提取视频信息（仅视频类型有此字段）
                    if (noteInfo.TryGetProperty("video", out var video))
                    {
                        // 提取时长
                        if (video.TryGetProperty("capa", out var capa) &&
                            capa.TryGetProperty("duration", out var duration))
                        {
                            var durationSeconds = duration.GetInt32();
                            detail.Duration = TimeSpan.FromSeconds(durationSeconds);
                        }

                        // 提取视频ID
                        if (video.TryGetProperty("media", out var media))
                        {
                            if (media.TryGetProperty("video", out var videoInfo) &&
                                videoInfo.TryGetProperty("bizId", out var bizId))
                            {
                                detail.VideoId = bizId.GetString();
                            }

                            // 提取视频流信息
                            if (media.TryGetProperty("stream", out var stream))
                            {
                                detail.VideoStream = new XhsVideoStream
                                {
                                    H264 = new List<XhsVideoQuality>(),
                                    H265 = new List<XhsVideoQuality>()
                                };

                                // 提取H264流
                                if (stream.TryGetProperty("h264", out var h264Array) && h264Array.GetArrayLength() > 0)
                                {
                                    for (int i = 0; i < h264Array.GetArrayLength(); i++)
                                    {
                                        var h264Stream = h264Array[i];
                                        var quality = ParseVideoQuality(h264Stream);
                                        if (quality != null)
                                        {
                                            detail.VideoStream.H264.Add(quality);
                                        }
                                    }

                                    // 使用第一个流作为默认播放URL
                                    if (detail.VideoStream.H264.Count > 0)
                                    {
                                        detail.VideoUrl = detail.VideoStream.H264[0].MasterUrl;
                                        detail.VideoSize = detail.VideoStream.H264[0].Size;
                                    }
                                }

                                // 提取H265流
                                if (stream.TryGetProperty("h265", out var h265Array) && h265Array.GetArrayLength() > 0)
                                {
                                    for (int i = 0; i < h265Array.GetArrayLength(); i++)
                                    {
                                        var h265Stream = h265Array[i];
                                        var quality = ParseVideoQuality(h265Stream);
                                        if (quality != null)
                                        {
                                            detail.VideoStream.H265.Add(quality);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                // 验证是否成功提取了关键信息
                bool hasVideo = !string.IsNullOrEmpty(detail.VideoUrl);
                bool hasImages = detail.Images != null && detail.Images.Count > 0;

                if (!string.IsNullOrEmpty(detail.Title) || hasVideo || hasImages)
                {
                    return detail;
                }
                else
                {
                    return null;
                }
            }
            catch (Exception ex)
            {AppLogger.Error("An error occurred while parsing XHS initial state JSON.", ex);
                
                return null;
            }
        }


        /// <summary>
        /// 解析视频质量信息
        /// </summary>
        private XhsVideoQuality? ParseVideoQuality(JsonElement qualityElement)
        {
            try
            {
                var quality = new XhsVideoQuality
                {
                    MasterUrl = NormalizeXhsVideoUrl(qualityElement.TryGetProperty("masterUrl", out var masterUrl) ? masterUrl.GetString() : null),
                    QualityType = qualityElement.TryGetProperty("qualityType", out var qualityType) ? qualityType.GetString() : null,
                    Width = qualityElement.TryGetProperty("width", out var width) ? width.GetInt32() : 0,
                    Height = qualityElement.TryGetProperty("height", out var height) ? height.GetInt32() : 0,
                    Duration = qualityElement.TryGetProperty("duration", out var duration) ? duration.GetInt32() : 0,
                    Size = qualityElement.TryGetProperty("size", out var size) ? size.GetInt64() : 0,
                    VideoBitrate = qualityElement.TryGetProperty("videoBitrate", out var videoBitrate) ? videoBitrate.GetInt32() : 0,
                    AudioBitrate = qualityElement.TryGetProperty("audioBitrate", out var audioBitrate) ? audioBitrate.GetInt32() : 0,
                    Fps = qualityElement.TryGetProperty("fps", out var fps) ? fps.GetInt32() : 0,
                    VideoCodec = qualityElement.TryGetProperty("videoCodec", out var videoCodec) ? videoCodec.GetString() : null,
                    AudioCodec = qualityElement.TryGetProperty("audioCodec", out var audioCodec) ? audioCodec.GetString() : null,
                    Format = qualityElement.TryGetProperty("format", out var format) ? format.GetString() : null,
                    BackupUrls = new List<string>()
                };

                // 提取备用URL
                if (qualityElement.TryGetProperty("backupUrls", out var backupUrls) && backupUrls.GetArrayLength() > 0)
                {
                    for (int i = 0; i < backupUrls.GetArrayLength(); i++)
                    {
                        var url = backupUrls[i].GetString();
                        if (!string.IsNullOrEmpty(url))
                        {
                            quality.BackupUrls.Add(NormalizeXhsVideoUrl(url));
                        }
                    }
                }

                return quality;
            }
            catch (Exception ex)
            {AppLogger.Error("An error occurred while parsing video quality.", ex);
                
                return null;
            }
        }

        /// <summary>
        /// 标准化小红书视频URL（统一替换为 sns-video-hw.xhscdn.com）
        /// 将 http 替换为 https，并将所有视频域名统一替换为 sns-video-hw.xhscdn.com
        /// </summary>
        private string? NormalizeXhsVideoUrl(string? url)
        {
            if (string.IsNullOrEmpty(url))
                return url;

            // 先将 http 替换为 https
            url = url.Replace("http://", "https://", StringComparison.OrdinalIgnoreCase);

            // 匹配小红书视频URL域名，统一替换为 sns-video-hw.xhscdn.com
            // 匹配格式：https://{任意字符}.xhscdn.com 或 https://{任意字符}.xiaohongshu.com
            var pattern = @"https://.*\.com";
            url = Regex.Replace(url, pattern, "https://sns-video-hw.xhscdn.com", RegexOptions.IgnoreCase);

            return url;
        }

        /// <summary>
        /// 标准化小红书图片URL（统一替换为 ci.xiaohongshu.com）
        /// 将 http 替换为 https，并移除日期时间目录，统一使用 ci.xiaohongshu.com
        /// 例如：https://sns-webpic-qc.xhscdn.com/202601261353/2c403d4aa55cc167aabdbacc96d8a209/xxx
        /// 转换为：https://ci.xiaohongshu.com/xxx
        /// </summary>
        private string? NormalizeXhsPicUrl(string? url)
        {
            if (string.IsNullOrEmpty(url))
                return url;

            // 先将 http 替换为 https
            url = url.Replace("http://", "https://", StringComparison.OrdinalIgnoreCase);

            // 匹配小红书图片URL，移除域名+日期时间目录
            // 模式1: https://sns-webpic-qc.xhscdn.com/202601261353/2c403d4aa55cc167aabdbacc96d8a209/xxx
            // 模式2: https://sns-webpic-qc.xhscdn.com/202601261404/aedecd1ba03f6c268b83528bcf696b6d/notes_pre_post/xxx
            // 通用模式: https://{域名}/{日期时间}/{哈希或路径}
            var pattern = @"https://.*\.com/\d+/[a-zA-Z0-9\-]*/";
            url = Regex.Replace(url, pattern, "https://ci.xiaohongshu.com/", RegexOptions.IgnoreCase);

            return url;
        }

        /// <summary>
        /// 解析小红书的计数字符串（如 "1万+"、"10+" 等）
        /// </summary>
        private long ParseCountString(string? countStr)
        {
            if (string.IsNullOrEmpty(countStr))
                return 0;

            try
            {
                // 移除 "+" 号
                countStr = countStr.Replace("+", "").Trim();

                // 处理 "万" 单位
                if (countStr.Contains("万"))
                {
                    var numStr = countStr.Replace("万", "").Trim();
                    if (double.TryParse(numStr, out var num))
                    {
                        return (long)(num * 10000);
                    }
                }

                // 处理 "千" 单位
                if (countStr.Contains("千"))
                {
                    var numStr = countStr.Replace("千", "").Trim();
                    if (double.TryParse(numStr, out var num))
                    {
                        return (long)(num * 1000);
                    }
                }

                // 直接解析数字
                if (long.TryParse(countStr, out var result))
                {
                    return result;
                }

                return 0;
            }
            catch
            {
                return 0;
            }
        }

    }



    /// <summary>
    /// 小红书视频详情模型
    /// </summary>
    public class XiaoHongShuVideoDetail
{
    public string? NoteId { get; set; }
    public string? VideoId { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? AuthorName { get; set; }
    public string? AuthorId { get; set; }
    public string? VideoUrl { get; set; }
    public string? CoverUrl { get; set; }
    public TimeSpan Duration { get; set; }
    public DateTime CreateTime { get; set; }
    public long? LikeCount { get; set; }
    public long? CollectCount { get; set; }
    public long? CommentCount { get; set; }
    public long? ShareCount { get; set; }

    /// <summary>
    /// 内容类型: "normal"=图文, "video"=视频
    /// </summary>
    public string? Type { get; set; }

    /// <summary>
    /// ContentType: 1=视频, 2=图文
    /// </summary>
    public int ContentType => Type == "video" ? 1 : 2;

    /// <summary>
    /// 图片列表（用于图文类型）
    /// </summary>
    public List<XhsImageItem>? Images { get; set; }

    /// <summary>
    /// 视频流信息（用于视频类型，支持多种清晰度）
    /// </summary>
    public XhsVideoStream? VideoStream { get; set; }

    /// <summary>
    /// 视频文件大小（字节）
    /// </summary>
    public long? VideoSize { get; set; }
}

/// <summary>
/// 小红书图片项
/// </summary>
public class XhsImageItem
{
    public int Width { get; set; }
    public int Height { get; set; }

    private string? _urlDefault;
    private string? _urlPre;

    public string? UrlDefault
    {
        get => _urlDefault;
        set => _urlDefault = NormalizeXhsPicUrlInternal(value);
    }

    public string? UrlPre
    {
        get => _urlPre;
        set => _urlPre = NormalizeXhsPicUrlInternal(value);
    }

    private List<string>? _infoList;
    public List<string>? InfoList
    {
        get => _infoList;
        set => _infoList = value?.Select(url => NormalizeXhsPicUrlInternal(url)).ToList();
    }

    /// <summary>
    /// 内部图片URL标准化方法（供属性setter使用）
    /// </summary>
    private static string? NormalizeXhsPicUrlInternal(string? url)
    {
        if (string.IsNullOrEmpty(url))
            return url;

        // 先将 http 替换为 https
        url = url.Replace("http://", "https://", StringComparison.OrdinalIgnoreCase);

        // 移除域名+日期时间目录
        var pattern = @"https://.*\.com/\d+/[a-zA-Z0-9\-]*/";
        url = Regex.Replace(url, pattern, "https://ci.xiaohongshu.com/", RegexOptions.IgnoreCase);

        return url;
    }
}
/// <summary>
/// 小红书视频流信息
/// </summary>
public class XhsVideoStream
    {
        /// <summary>
        /// H264编码的视频流
        /// </summary>
        public List<XhsVideoQuality>? H264 { get; set; }

        /// <summary>
        /// H265编码的视频流
        /// </summary>
        public List<XhsVideoQuality>? H265 { get; set; }
    }

    /// <summary>
    /// 小红书视频质量信息
    /// </summary>
    public class XhsVideoQuality
    {
        public string? MasterUrl { get; set; }
        public List<string>? BackupUrls { get; set; }
        public string? QualityType { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int Duration { get; set; }
        public long Size { get; set; }
        public int VideoBitrate { get; set; }
        public int AudioBitrate { get; set; }
        public int Fps { get; set; }
        public string? VideoCodec { get; set; }
        public string? AudioCodec { get; set; }
        public string? Format { get; set; }
    }
}
