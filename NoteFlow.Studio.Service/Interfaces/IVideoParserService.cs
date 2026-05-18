using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Playwright;
using NoteFlow.Studio.Service.Models;

namespace NoteFlow.Studio.Service.Interfaces;

public interface IVideoParserService
{
    /// <summary>
    /// 支持解析的平台名称
    /// </summary>
    string PlatformName { get; }

    /// <summary>
    /// 解析视频响应的 JSON 数据
    /// </summary>
    /// <param name="url">请求的 URL</param>
    /// <param name="jsonContent">响应内容</param>
    /// <returns>视频信息列表</returns>
    List<VideoInfo> ParseVideoInfo(string url, string jsonContent);

    /// <summary>
    /// 提取视频详情（通过 Playwright 背景抓取）
    /// </summary>
    /// <param name="videoId">视频 ID</param>
    /// <param name="cookies">浏览器继承的 Cookie</param>
    /// <returns>提取到的视频信息，以及是否增量更新或完成的标识等。这里可以直接组装好返回。</returns>
    Task<VideoInfo?> ExtractVideoDetailAsync(string videoId, IEnumerable<CookieDto> cookies, string address = "");

    /// <summary>
    /// 解析 yt-dlp 输出的 JSON
    /// </summary>
    VideoInfo? ParseYtDlpJson(string jsonContent);
}
