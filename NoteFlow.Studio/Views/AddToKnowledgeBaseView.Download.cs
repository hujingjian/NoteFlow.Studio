using NoteFlow.Studio.Infrastructure.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace NoteFlow.Studio.Views
{
    public partial class AddToKnowledgeBaseView
    {
        private async Task CheckFfmpegStatusAsync()
        {
            TxtFfmpegStatus.Text = "检测中...";
            TxtFfmpegStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(139, 143, 154)); // #8B8F9A
            BtnDownloadFfmpeg.Visibility = Visibility.Collapsed;

            bool hasFfmpeg = await Task.Run(() =>
            {
                // 1. Check system path
                var pathEnv = Environment.GetEnvironmentVariable("PATH");
                if (pathEnv != null)
                {
                    foreach (var path in pathEnv.Split(Path.PathSeparator))
                    {
                        var exePath = Path.Combine(path, "ffmpeg.exe");
                        if (File.Exists(exePath)) return true;
                    }
                }

                // 2. Check ToolDownloadPath
                var toolDir = GetToolDownloadPath();
                var localFfmpeg = Path.Combine(toolDir, "ffmpeg", "ffmpeg.exe");
                var localFfmpegBin = Path.Combine(toolDir, "ffmpeg", "bin", "ffmpeg.exe");
                if (File.Exists(localFfmpeg) || File.Exists(localFfmpegBin)) return true;

                return false;
            });

            if (hasFfmpeg)
            {
                TxtFfmpegStatus.Text = "已安装";
                TxtFfmpegStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 197, 94)); // Green
            }
            else
            {
                TxtFfmpegStatus.Text = "未安装";
                TxtFfmpegStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68)); // Red
                BtnDownloadFfmpeg.Visibility = Visibility.Visible;
            }
        }

        private readonly string[] _whisperModels = new[] { "ggml-base.bin", "ggml-small.bin", "ggml-medium.bin", "ggml-large-v3.bin" };

        private void InitWhisperModels()
        {
            CmbWhisperModel.Items.Clear();
            var dir = Path.Combine(GetToolDownloadPath(), "whisper");
            foreach (var model in _whisperModels)
            {
                bool exists = File.Exists(Path.Combine(dir, model));
                var item = new ComboBoxItem
                {
                    Content = model + (exists ? " (已下载)" : " (未下载)"),
                    Tag = model,
                    Foreground = exists ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 197, 94))
                                        : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(226, 226, 230)) // #E2E2E6
                };
                CmbWhisperModel.Items.Add(item);
            }
            if (CmbWhisperModel.Items.Count > 0)
            {
                CmbWhisperModel.SelectedIndex = 0;
            }
        }

        private void CmbWhisperModel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbWhisperModel.SelectedItem is ComboBoxItem item && item.Tag is string modelName)
            {
                var fpath = Path.Combine(GetToolDownloadPath(), "whisper", modelName);
                if (File.Exists(fpath))
                {
                    TxtWhisperModelStatus.Text = "已就绪";
                    TxtWhisperModelStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 197, 94));
                    BtnDownloadWhisperModel.Visibility = Visibility.Collapsed;
                }
                else
                {
                    TxtWhisperModelStatus.Text = "未下载";
                    TxtWhisperModelStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68));
                    BtnDownloadWhisperModel.Visibility = Visibility.Visible;
                }
            }
        }

        private async void BtnDownloadFfmpeg_Click(object sender, RoutedEventArgs e)
        {
            BtnDownloadFfmpeg.IsEnabled = false;
            ProgFfmpegDownload.Visibility = Visibility.Visible;
            ProgFfmpegDownload.Value = 0;
            TxtFfmpegStatus.Text = "下载解压中...";
            TxtFfmpegStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(139, 143, 154));

            try
            {
                string toolDir = Path.Combine(GetToolDownloadPath(), "ffmpeg");
                if (!Directory.Exists(toolDir)) Directory.CreateDirectory(toolDir);

                string zipPath = Path.Combine(toolDir, "ffmpeg.zip");
                // Simplified mock/real FFmpeg direct download for Windows win64 static
                string url = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-git-essentials.7z";

                using (var client = new System.Net.Http.HttpClient())
                {
                    using (var resp = await client.GetAsync(url, System.Net.Http.HttpCompletionOption.ResponseHeadersRead))
                    {
                        resp.EnsureSuccessStatusCode();
                        var totalBytes = resp.Content.Headers.ContentLength ?? -1L;
                        using (var srcStream = await resp.Content.ReadAsStreamAsync())
                        using (var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write))
                        {
                            var buffer = new byte[8192];
                            var isMoreToRead = true;
                            long totalRead = 0;

                            while (isMoreToRead)
                            {
                                var read = await srcStream.ReadAsync(buffer, 0, buffer.Length);
                                if (read == 0) isMoreToRead = false;
                                else
                                {
                                    await fs.WriteAsync(buffer, 0, read);
                                    totalRead += read;
                                    if (totalBytes != -1)
                                        ProgFfmpegDownload.Value = (double)totalRead / totalBytes * 100;
                                }
                            }
                        }
                    }
                }

                // Extract Zip
                TxtFfmpegStatus.Text = "正在解压...";
                await Task.Run(() =>
                {
                    System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, toolDir, true);
                    // Move contents of inner folder out (ffmpeg)
                    var extractedDir = Path.Combine(toolDir, "ffmpeg");
                    if (Directory.Exists(extractedDir))
                    {
                        foreach (var dir in Directory.GetDirectories(extractedDir))
                        {
                            var dest = Path.Combine(toolDir, Path.GetFileName(dir));
                            if (!Directory.Exists(dest)) Directory.Move(dir, dest);
                        }
                        Directory.Delete(extractedDir, true);
                    }
                    File.Delete(zipPath); // clean up
                });

                BtnDownloadFfmpeg.Visibility = Visibility.Collapsed;
                ProgFfmpegDownload.Visibility = Visibility.Collapsed;
                await CheckFfmpegStatusAsync(); // Re-verify
            }
            catch (Exception ex)
            {
                AppLogger.Error($"FFmpeg下载失败: {ex.Message}", ex);
                TxtFfmpegStatus.Text = $"下载失败: {ex.Message}";
                TxtFfmpegStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68));
                BtnDownloadFfmpeg.IsEnabled = true;
                ProgFfmpegDownload.Visibility = Visibility.Collapsed;
            }
        }

        private async void BtnDownloadWhisperModel_Click(object sender, RoutedEventArgs e)
        {
            if (CmbWhisperModel.SelectedItem is ComboBoxItem item && item.Tag is string modelName)
            {
                BtnDownloadWhisperModel.IsEnabled = false;
                ProgWhisperDownload.Visibility = Visibility.Visible;
                ProgWhisperDownload.Value = 0;
                TxtWhisperModelStatus.Text = "下载中...";
                TxtWhisperModelStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(139, 143, 154));
                CmbWhisperModel.IsEnabled = false;

                try
                {
                    string toolDir = Path.Combine(GetToolDownloadPath(), "whisper");
                    if (!Directory.Exists(toolDir)) Directory.CreateDirectory(toolDir);

                    string downloadPath = Path.Combine(toolDir, modelName);
                    string url = $"https://hf-mirror.com/ggerganov/whisper.cpp/resolve/main/{modelName}";

                    using (var client = new System.Net.Http.HttpClient())
                    {
                        using (var resp = await client.GetAsync(url, System.Net.Http.HttpCompletionOption.ResponseHeadersRead))
                        {
                            resp.EnsureSuccessStatusCode();
                            var totalBytes = resp.Content.Headers.ContentLength ?? -1L;
                            using (var srcStream = await resp.Content.ReadAsStreamAsync())
                            using (var fs = new FileStream(downloadPath, FileMode.Create, FileAccess.Write))
                            {
                                var buffer = new byte[8192];
                                var isMoreToRead = true;
                                long totalRead = 0;

                                while (isMoreToRead)
                                {
                                    var read = await srcStream.ReadAsync(buffer, 0, buffer.Length);
                                    if (read == 0) isMoreToRead = false;
                                    else
                                    {
                                        await fs.WriteAsync(buffer, 0, read);
                                        totalRead += read;
                                        if (totalBytes != -1)
                                            ProgWhisperDownload.Value = (double)totalRead / totalBytes * 100;
                                    }
                                }
                            }
                        }
                    }

                    ProgWhisperDownload.Visibility = Visibility.Collapsed;
                    BtnDownloadWhisperModel.IsEnabled = true;
                    CmbWhisperModel.IsEnabled = true;
                    InitWhisperModels(); // Refresh combo box texts

                    // Reselect the downloaded model
                    foreach (ComboBoxItem cbItem in CmbWhisperModel.Items)
                    {
                        if (cbItem.Tag.ToString() == modelName)
                        {
                            CmbWhisperModel.SelectedItem = cbItem;
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"Whisper模型下载失败: {ex.Message}", ex);
                    TxtWhisperModelStatus.Text = $"下载失败: {ex.Message}";
                    TxtWhisperModelStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68));
                    BtnDownloadWhisperModel.IsEnabled = true;
                    CmbWhisperModel.IsEnabled = true;
                    ProgWhisperDownload.Visibility = Visibility.Collapsed;
                }
            }
        }

    }
}
