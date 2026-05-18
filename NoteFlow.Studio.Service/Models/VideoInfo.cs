namespace NoteFlow.Studio.Service.Models;

public class VideoInfo
{
    public string Platform { get; set; } = string.Empty;
    public string PageUrl { get; set; } = string.Empty;
    public string VideoId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string CoverUrl { get; set; } = string.Empty;
    public string VideoUrl { get; set; } = string.Empty;
    public long Duration { get; set; }
    public string Description { get; set; } = string.Empty;
    public long PlayCount { get; set; }
    public long DiggCount { get; set; }
    public long CommentCount { get; set; }
    public long ShareCount { get; set; }
    public long CollectCount { get; set; }
    public long Size { get; set; }
    public string RawJson { get; set; } = string.Empty;
    public List<VideoStream> VideoStreams { get; set; } = new List<VideoStream>();
    public IEnumerable<CookieDto> Cookies { get; set; } = new List<CookieDto>();
}

public class VideoStream
{
    public string GearName { get; set; } = string.Empty;
    public string QualityType { get; set; } = string.Empty;
    public int Bitrate { get; set; }
    public string Url { get; set; } = string.Empty;
    public int VideoSize { get; set; }
}
