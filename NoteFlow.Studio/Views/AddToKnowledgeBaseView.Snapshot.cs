using NoteFlow.Studio.Infrastructure.Logging;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using NoteFlow.Studio.Repository.Config;
using NoteFlow.Studio.Model.Config;
using System.Windows.Input;

namespace NoteFlow.Studio.Views
{
    public partial class AddToKnowledgeBaseView
    {
        private async void BtnSnapshotAndOcr_Click(object sender, RoutedEventArgs e)
        {
            if (_mediaPlayer != null)
            {
                _mediaPlayer.SetPause(true); // Ensure it's paused without toggling to play
            }

            if (string.IsNullOrEmpty(_currentVideoUrl) || _mediaPlayer == null)
            {
                MessageBox.Show("请确保视频已准备就绪且已下载。");
                return;
            }

            try
            {
                BtnSnapshotAndOcr.IsEnabled = false;

                // 1. Get current time
                long timeMs = _mediaPlayer.Time;
                TimeSpan currentTs = TimeSpan.FromMilliseconds(timeMs);

                // 2. Setup path
                var configRepo = new ConfigRepository();
                string defaultDownloadDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Downloads");
                string baseDir = configRepo.GetConfig(AppConfigKeys.VideoDownloadPath, defaultDownloadDir);
                if (string.IsNullOrWhiteSpace(baseDir)) baseDir = defaultDownloadDir;

                string videoId = _currentVideoId;
                if (string.IsNullOrEmpty(videoId)) videoId = "unknown";

                string imageDir = Path.Combine(baseDir, "image", videoId);
                if (!Directory.Exists(imageDir)) Directory.CreateDirectory(imageDir);

                string imageFile = Path.Combine(imageDir, $"snapshot_{timeMs}.jpg");

                // 3. Ensure local video exists
                if (!File.Exists(_currentVideoUrl))
                {
                    MessageBox.Show("未找到本地视频文件，请先下载后操作。");
                    return;
                }

                // 4. Extract frame using ffmpeg
                string ffmpegPath = Path.Combine(GetToolDownloadPath(), "ffmpeg");
                if (!Directory.Exists(ffmpegPath))
                {
                    MessageBox.Show("FFmpeg 未准备好，请先等待配置。");
                    return;
                }
                
                Xabe.FFmpeg.FFmpeg.SetExecutablesPath(ffmpegPath);

                var conversion = Xabe.FFmpeg.FFmpeg.Conversions.New()
                    .AddParameter($"-ss {currentTs:hh\\:mm\\:ss\\.fff}")
                    .AddParameter($"-i \"{_currentVideoUrl}\"")
                    .AddParameter("-vframes 1")
                    .AddParameter("-q:v 2")
                    .SetOutput(imageFile);

                await conversion.Start();

                if (File.Exists(imageFile))
                {
                    _snapshotImagePaths.Add(imageFile);

                    // 5. Show image in history
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(imageFile);
                    bitmap.EndInit();

                    var container = new Grid
                    {
                        Margin = new Thickness(5)
                    };

                    var image = new Image
                    {
                        Source = bitmap,
                        Height = 60,
                        Cursor = Cursors.Hand
                    };

                    var deleteBtn = new Button
                    {
                        Content = "✕",
                        Width = 20,
                        Height = 20,
                        Padding = new Thickness(0),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Top,
                        Background = System.Windows.Media.Brushes.Red,
                        Foreground = System.Windows.Media.Brushes.White,
                        BorderThickness = new Thickness(0),
                        ToolTip = "删除此截图"
                    };

                    deleteBtn.Click += (s, args) =>
                    {
                        SnapshotHistoryPanel.Children.Remove(container);
                        _snapshotImagePaths.Remove(imageFile);
                        if (File.Exists(imageFile))
                        {
                            try { File.Delete(imageFile); } 
                            catch (Exception ex)
                            {
                                AppLogger.Error("删除旧截图文件异常", ex);
                            }
                        }
                    };

                    image.MouseLeftButtonUp += (s, args) =>
                    {
                        // Simple popup to show larger image centered on main window
                        var popup = new System.Windows.Controls.Primitives.Popup
                        {
                            PlacementTarget = Application.Current.MainWindow,
                            Placement = System.Windows.Controls.Primitives.PlacementMode.Center,
                            Child = new Border 
                            { 
                                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(200, 0, 0, 0)),
                                Padding = new Thickness(10),
                                CornerRadius = new CornerRadius(8),
                                Child = new Image { Source = bitmap, MaxHeight = 800, MaxWidth = 1000 }
                            },
                            StaysOpen = false,
                            AllowsTransparency = true,
                            PopupAnimation = System.Windows.Controls.Primitives.PopupAnimation.Fade
                        };
                        popup.MouseLeftButtonUp += (s2, a2) => popup.IsOpen = false;
                        popup.IsOpen = true;
                    };

                    container.Children.Add(image);
                    container.Children.Add(deleteBtn);
                    SnapshotHistoryPanel.Children.Add(container);

                    // 6. Perform OCR (full image)
                    var ocrService = new NoteFlow.Studio.Service.Providers.SubtitleOcrService(ffmpegPath);
                    string text = await ocrService.ExtractImageTextAsync(imageFile, 0, 0, bitmap.PixelWidth, bitmap.PixelHeight, bitmap.PixelWidth, bitmap.PixelHeight);

                    // 7. Append to result
                    ocrResultBuilder.AppendLine($"时间：\n[{currentTs:hh\\:mm\\:ss\\,fff}] 单帧截图识别：\n{text}\n 图片：[{imageFile}]\r\n");
                    TxtOcrResult.Text = ocrResultBuilder.ToString();
                    TxtOcrResult.ScrollToEnd();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error($"截图或单帧识别失败：{ex.Message}", ex);
                MessageBox.Show($"截图或单帧识别失败：{ex.Message}");
            }
            finally
            {
                BtnSnapshotAndOcr.IsEnabled = true;
            }
        }
    }
}

