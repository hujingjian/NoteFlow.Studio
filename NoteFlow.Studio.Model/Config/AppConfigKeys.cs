namespace NoteFlow.Studio.Model.Config
{
    public static class AppConfigKeys
    {
        /// <summary>
        /// 视频下载目录的配置键名
        /// </summary>
        public const string VideoDownloadPath = "VideoDownloadPath";

        /// <summary>
        /// 工具下载目录的配置键名（例如ffmpeg，whisper等）
        /// </summary>
        public const string ToolDownloadPath = "ToolDownloadPath";

        /// <summary>
        /// CLI启动目录
        /// </summary>
        public const string CliStartupPath = "D:\\Downloads";

        /// <summary>
        /// Obsidian 仓库根目录配置
        /// </summary>
        public const string ObsidianRootPath = "ObsidianRootPath";
    }
}