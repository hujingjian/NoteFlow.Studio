using LibVLCSharp.Shared;
using NoteFlow.Studio.Model.Config;
using NoteFlow.Studio.Repository.Config;
using NoteFlow.Studio.Service.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace NoteFlow.Studio.Views
{
    public partial class AddToKnowledgeBaseView
    {

        private void MediaPlayer_PositionChanged(object sender, MediaPlayerPositionChangedEventArgs e)
        {
            if (!_isDraggingSlider)
            {
                try
                {
                    Dispatcher.Invoke(() => VideoSlider.Value = e.Position);
                }
                catch (TaskCanceledException)
                {
                    // 窗口关闭时，Dispatcher.Invoke 可能会引发此异常，直接忽略
                }
            }
        }

        private void MediaPlayer_TimeChanged(object sender, MediaPlayerTimeChangedEventArgs e)
        {
            try
            {
                Dispatcher.Invoke(UpdateVideoTimeText);
            }
            catch (TaskCanceledException)
            {
                // 窗口关闭时，Dispatcher.Invoke 可能会引发此异常，直接忽略
            }
        }

        private void MediaPlayer_LengthChanged(object sender, MediaPlayerLengthChangedEventArgs e)
        {
            try
            {
                Dispatcher.Invoke(UpdateVideoTimeText);
            }
            catch (TaskCanceledException)
            {
                // 窗口关闭时，Dispatcher.Invoke 可能会引发此异常，直接忽略
            }
        }

        private void UpdateVideoTimeText()
        {
            if (_mediaPlayer == null) return;
            string cur = TimeSpan.FromMilliseconds(_mediaPlayer.Time).ToString(@"hh\:mm\:ss");
            string total = TimeSpan.FromMilliseconds(_mediaPlayer.Length).ToString(@"hh\:mm\:ss");
            TxtVideoTime.Text = $"{cur} / {total}";
        }

        public async void SetVideoInfo(VideoInfo info)
        {
            // 停止并清除之前的播放内容
            if (_mediaPlayer.IsPlaying)
            {
                _mediaPlayer.Stop();
            }
            _mediaPlayer.Media = null;

            if (info != null && !string.IsNullOrEmpty(info.VideoUrl))
            {
                string title = info.Title.Replace("\n", "");
                TxtTitle.Text = string.IsNullOrEmpty(title) ? "加入知识库: " : "加入知识库: " + (title.Length > 50 ? title.Substring(0, 50) + "..." : title);
                TxtPlatform.Text = info.Platform ?? "未知";
                TxtDuration.Text = FormatDuration(info.Duration);

                // Add fake file size or handle format internally later if provided
                TxtSize.Text = FormatSize(info.Size);

                _pageUrl = info.PageUrl;
                _cookies = info.Cookies;
                _onlineVideoUrl = info.VideoUrl;
                _currentVideoId = info.VideoId;
                _currentVideoPlatform = info.Platform;

                var configRepo = new ConfigRepository();
                string defaultDownloadDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Downloads");
                string downloadDir = configRepo.GetConfig(AppConfigKeys.VideoDownloadPath, defaultDownloadDir);
                if (string.IsNullOrWhiteSpace(downloadDir)) downloadDir = defaultDownloadDir;

                string localFileName = string.IsNullOrEmpty(_currentVideoId) ? "" : $"{_currentVideoId}.mp4";
                string localPath = string.IsNullOrEmpty(localFileName) ? "" : Path.Combine(downloadDir, localFileName);

                if (!string.IsNullOrEmpty(localPath) && File.Exists(localPath))
                {
                    _currentVideoUrl = localPath;
                    TxtVideoStatus.Text = "视频准备就绪（已下载）。";
                    TxtStep1Desc.Text = "已下载";
                    ProgStep1.Value = 100;
                    IconStep1.Kind = MaterialDesignThemes.Wpf.PackIconKind.CheckCircle;
                    IconStep1.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 197, 94)); // #22C55E
                }
                else
                {
                    _currentVideoUrl = _onlineVideoUrl;
                    TxtVideoStatus.Text = "未下载，等待下载...（可点击开始任务）";
                    TxtStep1Desc.Text = "等待下载";
                    ProgStep1.Value = 0;
                    IconStep1.Kind = MaterialDesignThemes.Wpf.PackIconKind.ClockOutline;
                    IconStep1.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(139, 143, 154)); // #8B8F9A
                }

                // 检查YouTube字幕
                await CheckYouTubeSubtitles();

                // Play first stream directly for preview if it's not already playing it
                bool isAlreadyPlaying = _mediaPlayer.Media != null &&
                    (Uri.UnescapeDataString(_mediaPlayer.Media.Mrl) == new Uri(_currentVideoUrl).ToString() ||
                     Uri.UnescapeDataString(_mediaPlayer.Media.Mrl) == new Uri(_onlineVideoUrl).ToString());

                if (!isAlreadyPlaying)
                {
                    VideoPlayer.MediaPlayer = _mediaPlayer; // 提前将 MediaPlayer 实例绑定到 VideoView
                    var media = new LibVLCSharp.Shared.Media(_libVLC, new Uri(_currentVideoUrl));
                    if (_currentVideoUrl.Contains("bilibili") || _currentVideoUrl.Contains("bilivideo") || _currentVideoUrl.Contains("bilivideo.cn"))
                    {
                        media.AddOption(":http-referrer=https://www.bilibili.com");
                        media.AddOption(":http-user-agent=Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120.0.0.0 Safari/537.36");
                    }
                    if (!_currentVideoUrl.ToLower().Contains("youtube"))
                    {
                        _mediaPlayer.Play(media);
                    }
                }

                if (_currentVideoUrl.ToLower().Contains("youtube"))
                {
                    VideoPlayer.Visibility = Visibility.Collapsed;
                    PanelVideoControls.Visibility = Visibility.Collapsed;
                    TxtVideoStatus.Text = "视频未下载，请先开始任务以下载视频。";
                }
                else
                {
                    VideoPlayer.Visibility = Visibility.Visible;
                    PanelVideoControls.Visibility = Visibility.Visible;
                }
                BtnToggleTask.IsEnabled = true;
            }
            else
            {
                TxtTitle.Text = "加入知识库: 暂无视频";
                TxtPlatform.Text = "--";
                TxtDuration.Text = "--";
                TxtSize.Text = "--";

                TxtVideoStatus.Text = "未获取到有效视频URL。";
                //VideoPlayer.Visibility = Visibility.Collapsed;
                PanelVideoControls.Visibility = Visibility.Collapsed;
            }
        }

        private async Task CheckYouTubeSubtitles()
        {
            CmbItemYtDlpSubtitle.Visibility = Visibility.Collapsed;
            PanelYtSubtitleLanguage.Visibility = Visibility.Collapsed;
            CmbYtSubtitleLanguage.Items.Clear();

            if (_currentVideoPlatform == "YouTube")
            {
                var youtubeService = new Service.VideoParsers.YouTubeVideoParserService();
                try
                {
                    var tracks = await youtubeService.GetCaptionTracksAsync(_currentVideoId, _cookies);
                    var availableTracks = tracks.Where(t => t.Language.Code == "en" || t.Language.Code.StartsWith("zh-Hans")).ToList();

                    if (availableTracks.Any())
                    {
                        CmbItemYtDlpSubtitle.Visibility = Visibility.Visible;
                        PanelYtSubtitleLanguage.Visibility = Visibility.Visible;

                        foreach (var track in availableTracks)
                        {
                            var comboBoxItem = new ComboBoxItem
                            {
                                Content = track.Language.Name,
                                Tag = track.Language
                            };
                            CmbYtSubtitleLanguage.Items.Add(comboBoxItem);
                        }

                        // Default to English if available
                        var englishItem = CmbYtSubtitleLanguage.Items.OfType<ComboBoxItem>().FirstOrDefault(i => ((YoutubeExplode.Videos.ClosedCaptions.Language)i.Tag).Code == "en");
                        if (englishItem != null)
                        {
                            CmbYtSubtitleLanguage.SelectedItem = englishItem;
                        }
                        else
                        {
                            CmbYtSubtitleLanguage.SelectedIndex = 0;
                        }
                        
                        CmbExtractMethod.SelectedItem = CmbItemYtDlpSubtitle;
                    }
                }
                catch (Exception ex)
                {
                    // Log error
                    Console.WriteLine($"Error checking youtube subtitles: {ex.Message}");
                }
            }

            if (CmbItemYtDlpSubtitle.Visibility == Visibility.Collapsed && CmbItemYtDlpSubtitle.IsSelected)
            {
                CmbExtractMethod.SelectedIndex = 0;
            }
        }

        public void ResetVideoInfo()
        {
            _pageUrl = "";
            _cookies = null;
            _onlineVideoUrl = "";
            _currentVideoId = "";
            _currentVideoPlatform = "";
            VideoPlayer.Visibility = Visibility.Collapsed;
            VideoPlayer.MediaPlayer = null;

            // Reset task status
            _isTaskRunning = false;
            BtnToggleTask.IsEnabled = false;
            BtnToggleTask.Content = "开始任务";
            BtnToggleTask.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#414DE5"));

            // Reset step indicators
            TxtStep1Desc.Text = "等待任务开始...";
            ProgStep1.Value = 0;
            IconStep1.Kind = MaterialDesignThemes.Wpf.PackIconKind.CircleOutline;
            IconStep1.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(58, 58, 73)); // #3A3A49

            TxtStep2Desc.Text = "准备识别...";
            ProgStep2.Value = 0;
            ProgStep2.Visibility = Visibility.Collapsed;
            IconStep2.Kind = MaterialDesignThemes.Wpf.PackIconKind.CircleOutline;
            IconStep2.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(58, 58, 73)); // #3A3A49

            TxtStep3Desc.Text = "准备生成...";
            IconStep3.Kind = MaterialDesignThemes.Wpf.PackIconKind.CircleOutline;
            IconStep3.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(58, 58, 73)); // #3A3A49

            TxtStep4Desc.Text = "未开始...";
            IconStep4.Kind = MaterialDesignThemes.Wpf.PackIconKind.CircleOutline;
            IconStep4.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(58, 58, 73)); // #3A3A49

            // Reset OCR result
            TxtOcrResult.Text = "";
            TxtPureOcrResult.Text = "";
            TxtMarkdownEditor.Text = "";
            MarkdownViewer.NavigateToString(" ");

            // Reset crop area
            _isDrawing = false;
            _canDraw = false;
            CropOverlayPopup.IsOpen = false;
            if (CropRectangle != null)
            {
                CropRectangle.Visibility = Visibility.Collapsed;
                CropRectangle.Width = 0;
                CropRectangle.Height = 0;
            }
            TxtCropArea.Text = "尚未框选";
            BtnConfirmCropArea.Visibility = Visibility.Collapsed;

            // Reset snapshot preview
            SnapshotHistoryPanel.Children.Clear();
            _snapshotImagePaths.Clear();

            // Reset extraction method
            CmbExtractMethod.SelectedIndex = 0;
        }

        private void VideoPlayer_Unloaded(object sender, RoutedEventArgs e)
        {
            _mediaPlayer?.Stop();
            _mediaPlayer?.Dispose();
            _libVLC?.Dispose();
        }

        private void BtnPlay_Click(object sender, RoutedEventArgs e) => _mediaPlayer.Play();
        private void BtnPause_Click(object sender, RoutedEventArgs e) => _mediaPlayer.Pause();
        private void BtnStop_Click(object sender, RoutedEventArgs e) => _mediaPlayer.Stop();

        private bool _isDraggingSlider = false;

        private void VideoSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDraggingSlider = true;
        }

        private void VideoSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isDraggingSlider = false;
            if (_mediaPlayer != null)
            {
                _mediaPlayer.Position = (float)VideoSlider.Value;
            }
        }

        private void VideoSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // This check is to prevent setting the position while the user is dragging the slider.
            if (_isDraggingSlider)
            {
                // Optionally, you could update a time preview label here.
            }
        }

        private void BtnRewind_Click(object sender, RoutedEventArgs e)
        {
            if (_mediaPlayer.IsSeekable)
                _mediaPlayer.Time -= 3000; // Rewind 3 seconds
        }

        private void BtnFastForward_Click(object sender, RoutedEventArgs e)
        {
            if (_mediaPlayer.IsSeekable)
                _mediaPlayer.Time += 3000; // Fast forward 3 seconds
        }

        private bool _isPlayerEnlarged = false;
        private void BtnToggleFullScreen_Click(object sender, RoutedEventArgs e)
        {
            _isPlayerEnlarged = !_isPlayerEnlarged;
            var icon = IconFullScreen;

            if (_isPlayerEnlarged)
            {
                // 放大高度
                PlayerBorder.Height = 500;

                // Enlarge the player: hide the right sidebar
                PlayerColumn.Width = new GridLength(1, GridUnitType.Star);
                SplitterColumn.Width = new GridLength(0);
                SidebarColumn.Width = new GridLength(0);

                // 调整内部列占比，增加视频列的宽度
                InnerVideoColumn.Width = new GridLength(2, GridUnitType.Star);
                InnerOcrColumn.Width = new GridLength(1, GridUnitType.Star);

                if (icon != null)
                {
                    icon.Kind = MaterialDesignThemes.Wpf.PackIconKind.FullscreenExit;
                }
                BtnToggleFullScreen.ToolTip = "还原视图";
            }
            else
            {
                // 恢复默认高度
                PlayerBorder.Height = 250;

                // Restore to normal size
                PlayerColumn.Width = new GridLength(3, GridUnitType.Star);
                SplitterColumn.Width = new GridLength(20);
                SidebarColumn.Width = new GridLength(1, GridUnitType.Star);
                
                // 恢复内部列占比
                InnerVideoColumn.Width = new GridLength(1, GridUnitType.Star);
                InnerOcrColumn.Width = new GridLength(1.5, GridUnitType.Star);

                if (icon != null)
                {
                    icon.Kind = MaterialDesignThemes.Wpf.PackIconKind.Fullscreen;
                }
                BtnToggleFullScreen.ToolTip = "影院模式";
            }
        }
    }
}
