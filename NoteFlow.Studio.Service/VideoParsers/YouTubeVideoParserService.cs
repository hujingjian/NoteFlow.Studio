using NoteFlow.Studio.Service.Interfaces;
using NoteFlow.Studio.Service.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NoteFlow.Studio.Infrastructure.Logging;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;
using YoutubeExplode.Videos.ClosedCaptions;
using System.Text;

namespace NoteFlow.Studio.Service.VideoParsers
{
    public class YouTubeVideoParserService : IVideoParserService
    {
        public string PlatformName => "YouTube";
        public string Platform => "YouTube";

        private YoutubeClient GetYoutubeClient(IEnumerable<CookieDto>? cookies)
        {
            var cookieContainer = new CookieContainer();
            if (cookies != null)
            {
                foreach (var cookie in cookies)
                {
                    cookieContainer.Add(new Cookie(cookie.Name, cookie.Value, cookie.Path, cookie.Domain));
                }
            }

            var handler = new HttpClientHandler { CookieContainer = cookieContainer };
            var httpClient = new HttpClient(handler);
            return new YoutubeClient(httpClient);
        }

        public async Task<VideoInfo?> ExtractVideoDetailAsync(string videoId, IEnumerable<CookieDto> cookies, string address = "")
        {
            try
            {
                var youtube = GetYoutubeClient(cookies);
                var video = await youtube.Videos.GetAsync(videoId);

                var videoInfo = new VideoInfo
                {
                    Platform = Platform,
                    VideoId = video.Id,
                    Title = video.Title,
                    Description = video.Description,
                    Author = video.Author.ChannelTitle,
                    PlayCount = video.Engagement.ViewCount,
                    DiggCount = video.Engagement.LikeCount,
                    VideoUrl = video.Url,
                    CoverUrl = video.Thumbnails.FirstOrDefault()?.Url,
                    Duration = (long)(video.Duration?.TotalSeconds ?? 0),
                    PageUrl = address,
                    Cookies = cookies
                };
                return videoInfo;
            }
            catch (Exception ex)
            {
                AppLogger.Error($"YouTubeΩ‚Œˆ“Ï≥£: {ex.Message}", ex);
            }

            return null;
        }

        public async Task<IEnumerable<ClosedCaptionTrackInfo>> GetCaptionTracksAsync(string videoId, IEnumerable<CookieDto> cookies)
        {
            var youtube = GetYoutubeClient(cookies);
            var trackManifest = await youtube.Videos.ClosedCaptions.GetManifestAsync(videoId);
            return trackManifest.Tracks;
        }

        public async Task<(string, string)> GetCaptionAsync(string videoId, Language language, IEnumerable<CookieDto> cookies)
        {
            var youtube = GetYoutubeClient(cookies);
            var trackManifest = await youtube.Videos.ClosedCaptions.GetManifestAsync(videoId);
            var trackInfo = trackManifest.GetByLanguage(language.Code);

            if (trackInfo == null)
            {
                return (string.Empty, string.Empty);
            }

            var captions = await youtube.Videos.ClosedCaptions.GetAsync(trackInfo);
            StringBuilder srtTxt = new StringBuilder();
            StringBuilder txt = new StringBuilder();
            foreach (var caption in captions.Captions.Where(x => x.Parts.Count > 0))
            {
                srtTxt.Append($"[{caption.Offset} -> {caption.Offset.Add(caption.Duration)}] {caption.Text} \r\n");
                txt.Append($" {caption.Text} \r\n");
            }
            return (srtTxt.ToString(), txt.ToString());
        }

        public async Task DownloadVideoAsync(string videoId, string outputFilePath, IEnumerable<CookieDto> cookies, IProgress<double>? progress = null)
        {
            var youtube = GetYoutubeClient(cookies);
            var streamManifest = await youtube.Videos.Streams.GetManifestAsync(videoId);
            var streamInfo = streamManifest.GetMuxedStreams().GetWithHighestVideoQuality();

            if (streamInfo != null)
            {
                await youtube.Videos.Streams.DownloadAsync(streamInfo, outputFilePath, progress);
            }
            else
            {
                throw new NotSupportedException("No muxed streams found for this video.");
            }
        }

        public List<VideoInfo> ParseVideoInfo(string url, string jsonContent)
        {
            // This method might not be relevant anymore with YoutubeExplode
            return new List<VideoInfo>();
        }

        public VideoInfo? ParseYtDlpJson(string jsonContent)
        {
            // This method might not be relevant anymore with YoutubeExplode
            try
            {
                var doc = System.Text.Json.JsonDocument.Parse(jsonContent).RootElement;
                return new VideoInfo
                {
                    Platform = Platform,
                    VideoId = doc.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                    Title = doc.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
                    Description = doc.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "",
                    Author = doc.TryGetProperty("uploader", out var u) ? u.GetString() ?? "" : "",
                    PlayCount = doc.TryGetProperty("view_count", out var vc) && vc.ValueKind == System.Text.Json.JsonValueKind.Number ? vc.GetInt64() : 0,
                    DiggCount = doc.TryGetProperty("like_count", out var lc) && lc.ValueKind == System.Text.Json.JsonValueKind.Number ? lc.GetInt64() : 0,
                    VideoUrl = doc.TryGetProperty("webpage_url", out var vu) ? vu.GetString() ?? "" : "",
                    CoverUrl = doc.TryGetProperty("thumbnail", out var th) ? th.GetString() ?? "" : "",
                    Size = doc.TryGetProperty("filesize", out var fs) && fs.ValueKind == System.Text.Json.JsonValueKind.Number ? fs.GetInt64() : 0,
                    Duration = doc.TryGetProperty("duration", out var dur) && dur.ValueKind == System.Text.Json.JsonValueKind.Number ? dur.GetInt64() : 0,
                };
            }
            catch
            {
                return null;
            }
        }
    }
}
