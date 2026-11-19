using LibVLCSharp.Shared;
using Microsoft.Win32;
using NoteFlow.Studio.Model.Config;
using NoteFlow.Studio.Repository.Config;
using NoteFlow.Studio.Service.Providers;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace NoteFlow.Studio.Views
{
    public partial class VideoRecreationView : UserControl, IDisposable
    {
        private const int MaxAnalysisFrames = 120;
        private readonly LibVLC _libVlc;
        private readonly MediaPlayer _mediaPlayer;
        private string _videoPath = string.Empty;
        private double _durationMs;
        private bool _isScrubbing;
        private bool _updatingSelection;
        private bool _disposed;
        private int _sourceWidth;
        private int _sourceHeight;
        private CropRegion _cropRegion = CropRegion.Full;
        private System.Windows.Rect _draftCropBounds;
        private System.Windows.Point _cropDragStart;
        private bool _isSelectingCrop;

        private sealed record CropRegion(double X, double Y, double Width, double Height)
        {
            public static CropRegion Full { get; } = new(0, 0, 1, 1);
            public bool IsFull => X < 0.001 && Y < 0.001 && Width > 0.998 && Height > 0.998;
        }

        public VideoRecreationView()
        {
            InitializeComponent();
            Core.Initialize();
            _libVlc = new LibVLC();
            _mediaPlayer = new MediaPlayer(_libVlc);
            VideoPlayer.MediaPlayer = _mediaPlayer;

            _mediaPlayer.TimeChanged += MediaPlayer_TimeChanged;
            _mediaPlayer.Playing += (_, _) => Dispatcher.BeginInvoke(() => PlayPauseIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.Pause);
            _mediaPlayer.Paused += (_, _) => Dispatcher.BeginInvoke(() => PlayPauseIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.Play);
            _mediaPlayer.Stopped += (_, _) => Dispatcher.BeginInvoke(() => PlayPauseIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.Play);

            RefreshModels();
        }

        public void RefreshModels()
        {
            var models = new AiModelRepository().GetAll().Where(m => m.IsEnabled).ToList();
            CmbVisionModel.ItemsSource = models;
            if (models.Count > 0) CmbVisionModel.SelectedIndex = 0;
            BtnAnalyze.IsEnabled = !string.IsNullOrWhiteSpace(_videoPath) && models.Count > 0;
        }

        private async void BtnOpenVideo_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择参考视频",
                Filter = "视频文件|*.mp4;*.mov;*.mkv;*.avi;*.webm;*.m4v|所有文件|*.*"
            };
            if (dialog.ShowDialog() != true) return;

            await LoadVideoAsync(dialog.FileName);
        }

        private async Task LoadVideoAsync(string path)
        {
            SetBusy(true, "正在读取视频与生成时间轴缩略图…", false);
            _videoPath = path;
            _mediaPlayer.Stop();

            try
            {
                var metadata = await Task.Run(() => ReadVideoMetadataAndThumbnails(path, 12));
                _durationMs = metadata.DurationMs;
                if (_durationMs <= 0) throw new InvalidOperationException("无法读取视频时长，请确认文件可以正常播放。");
                _sourceWidth = metadata.Width;
                _sourceHeight = metadata.Height;
                _cropRegion = CropRegion.Full;
                TxtCropSummary.Text = "全画面";

                _updatingSelection = true;
                PlaybackSlider.Maximum = _durationMs;
                InPointSlider.Maximum = _durationMs;
                OutPointSlider.Maximum = _durationMs;
                PlaybackSlider.Value = 0;
                InPointSlider.Value = 0;
                OutPointSlider.Value = _durationMs;
                _updatingSelection = false;
                UpdateSelectionLabels();

                ThumbnailStrip.ItemsSource = metadata.Thumbnails;
                EmptyVideoState.Visibility = Visibility.Collapsed;
                BtnPlayPause.IsEnabled = true;
                PlaybackSlider.IsEnabled = true;
                InPointSlider.IsEnabled = true;
                OutPointSlider.IsEnabled = true;
                BtnCrop.IsEnabled = true;
                BtnAnalyze.IsEnabled = CmbVisionModel.Items.Count > 0;

                using var media = new Media(_libVlc, Path.GetFullPath(path), FromType.FromPath);
                _mediaPlayer.Play(media);
                await Task.Delay(180);
                _mediaPlayer.Pause();
                _mediaPlayer.Time = 0;
                UpdateTimelineVisuals();
                UpdateTimeLabels(0);

                TxtStatus.Text = CmbVisionModel.Items.Count > 0
                    ? $"已载入 {Path.GetFileName(path)}，拖动入点和出点选择片段。"
                    : "视频已载入，但没有启用的大模型；请先到“大模型设置”添加视觉模型。";
            }
            catch (Exception ex)
            {
                _videoPath = string.Empty;
                TxtStatus.Text = $"载入失败：{ex.Message}";
                MessageBox.Show(ex.Message, "无法打开视频", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                SetBusy(false, TxtStatus.Text, false);
            }
        }

        private static (double DurationMs, int Width, int Height, List<BitmapImage> Thumbnails) ReadVideoMetadataAndThumbnails(string path, int count)
        {
            using var capture = new VideoCapture(path);
            if (!capture.IsOpened()) throw new InvalidOperationException("视频解码器无法打开此文件。");

            var fps = capture.Get(VideoCaptureProperties.Fps);
            var frameCount = capture.Get(VideoCaptureProperties.FrameCount);
            var durationMs = fps > 0 ? frameCount / fps * 1000d : 0;
            var thumbnails = new List<BitmapImage>();

            for (var i = 0; i < count; i++)
            {
                var time = count == 1 ? 0 : durationMs * i / (count - 1d);
                capture.Set(VideoCaptureProperties.PosMsec, time);
                using var frame = new Mat();
                if (!capture.Read(frame) || frame.Empty()) continue;
                using var resized = ResizeForUpload(frame, 240);
                Cv2.ImEncode(".jpg", resized, out var bytes, new[] { (int)ImwriteFlags.JpegQuality, 72 });
                thumbnails.Add(ToBitmapImage(bytes));
            }

            return (durationMs, (int)capture.Get(VideoCaptureProperties.FrameWidth), (int)capture.Get(VideoCaptureProperties.FrameHeight), thumbnails);
        }

        private static Mat ResizeForUpload(Mat source, int maxWidth)
        {
            if (source.Width <= maxWidth) return source.Clone();
            var height = Math.Max(1, (int)Math.Round(source.Height * maxWidth / (double)source.Width));
            var result = new Mat();
            Cv2.Resize(source, result, new OpenCvSharp.Size(maxWidth, height), 0, 0, InterpolationFlags.Area);
            return result;
        }

        private static BitmapImage ToBitmapImage(byte[] bytes)
        {
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }

        private void MediaPlayer_TimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (!_isScrubbing) PlaybackSlider.Value = Math.Clamp(e.Time, 0, _durationMs);
                if (ChkLoopSelection.IsChecked == true && e.Time >= OutPointSlider.Value && OutPointSlider.Value > InPointSlider.Value)
                {
                    _mediaPlayer.Time = (long)InPointSlider.Value;
                }
                UpdateTimeLabels(e.Time);
                UpdateTimelineVisuals();
            });
        }

        private void BtnPlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (_mediaPlayer.IsPlaying) _mediaPlayer.Pause();
            else
            {
                if (_mediaPlayer.Time < InPointSlider.Value || _mediaPlayer.Time >= OutPointSlider.Value)
                    _mediaPlayer.Time = (long)InPointSlider.Value;
                _mediaPlayer.Play();
            }
        }

        private void BtnJumpBack_Click(object sender, RoutedEventArgs e) => SeekTo(_mediaPlayer.Time - 1000);
        private void BtnJumpForward_Click(object sender, RoutedEventArgs e) => SeekTo(_mediaPlayer.Time + 1000);

        private void SeekTo(double milliseconds)
        {
            if (_durationMs <= 0) return;
            var value = Math.Clamp(milliseconds, 0, _durationMs);
            _mediaPlayer.Time = (long)value;
            PlaybackSlider.Value = value;
        }

        private void PlaybackSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _isScrubbing = true;
        private void PlaybackSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isScrubbing = false;
            SeekTo(PlaybackSlider.Value);
        }

        private void PlaybackSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isScrubbing) UpdateTimeLabels(e.NewValue);
            UpdateTimelineVisuals();
        }

        private void InPointSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updatingSelection || OutPointSlider == null) return;
            if (InPointSlider.Value > OutPointSlider.Value - 100)
            {
                _updatingSelection = true;
                InPointSlider.Value = Math.Max(0, OutPointSlider.Value - 100);
                _updatingSelection = false;
            }
            SeekTo(InPointSlider.Value);
            UpdateSelectionLabels();
        }

        private void OutPointSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updatingSelection || InPointSlider == null) return;
            if (OutPointSlider.Value < InPointSlider.Value + 100)
            {
                _updatingSelection = true;
                OutPointSlider.Value = Math.Min(_durationMs, InPointSlider.Value + 100);
                _updatingSelection = false;
            }
            SeekTo(OutPointSlider.Value);
            UpdateSelectionLabels();
        }

        private void UpdateSelectionLabels()
        {
            TxtInPoint.Text = FormatTime(InPointSlider.Value);
            TxtOutPoint.Text = FormatTime(OutPointSlider.Value);
            TxtSelectionDuration.Text = $"已选 {FormatTime(Math.Max(0, OutPointSlider.Value - InPointSlider.Value))}";
            UpdateTimelineVisuals();
        }

        private void UpdateTimeLabels(double current)
        {
            TxtPlayerTime.Text = $"{FormatTime(current)} / {FormatTime(_durationMs)}";
        }

        private static string FormatTime(double milliseconds)
        {
            var time = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
            return time.TotalHours >= 1 ? time.ToString(@"hh\:mm\:ss\.fff") : time.ToString(@"mm\:ss\.fff");
        }

        private void TimelineHost_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateTimelineVisuals();

        private void VideoContainer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (CropPopup.IsOpen) ShowCurrentCropInCanvas();
        }

        private void BtnCrop_Click(object sender, RoutedEventArgs e)
        {
            if (_sourceWidth <= 0 || _sourceHeight <= 0) return;
            PauseVideo();
            CropPopup.IsOpen = true;
            Dispatcher.BeginInvoke(ShowCurrentCropInCanvas, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void ShowCurrentCropInCanvas()
        {
            var videoRect = GetDisplayedVideoRect();
            if (videoRect.Width <= 0 || videoRect.Height <= 0) return;
            _draftCropBounds = new System.Windows.Rect(
                videoRect.X + _cropRegion.X * videoRect.Width,
                videoRect.Y + _cropRegion.Y * videoRect.Height,
                _cropRegion.Width * videoRect.Width,
                _cropRegion.Height * videoRect.Height);
            DrawCropRectangle();
        }

        private System.Windows.Rect GetDisplayedVideoRect()
        {
            var canvasWidth = CropCanvas.ActualWidth > 0 ? CropCanvas.ActualWidth : VideoContainer.ActualWidth;
            var canvasHeight = CropCanvas.ActualHeight > 0 ? CropCanvas.ActualHeight : VideoContainer.ActualHeight;
            if (canvasWidth <= 0 || canvasHeight <= 0 || _sourceWidth <= 0 || _sourceHeight <= 0)
                return System.Windows.Rect.Empty;

            var videoAspect = _sourceWidth / (double)_sourceHeight;
            var containerAspect = canvasWidth / canvasHeight;
            if (containerAspect > videoAspect)
            {
                var width = canvasHeight * videoAspect;
                return new System.Windows.Rect((canvasWidth - width) / 2d, 0, width, canvasHeight);
            }

            var height = canvasWidth / videoAspect;
            return new System.Windows.Rect(0, (canvasHeight - height) / 2d, canvasWidth, height);
        }

        private void CropCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var videoRect = GetDisplayedVideoRect();
            var rawPoint = e.GetPosition(CropCanvas);
            if (!videoRect.Contains(rawPoint)) return;
            var point = ClampToRect(rawPoint, videoRect);
            _cropDragStart = point;
            _draftCropBounds = new System.Windows.Rect(point, point);
            _isSelectingCrop = true;
            CropCanvas.CaptureMouse();
            DrawCropRectangle();
        }

        private void CropCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isSelectingCrop || e.LeftButton != MouseButtonState.Pressed) return;
            var point = ClampToRect(e.GetPosition(CropCanvas), GetDisplayedVideoRect());
            _draftCropBounds = new System.Windows.Rect(_cropDragStart, point);
            DrawCropRectangle();
        }

        private void CropCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isSelectingCrop) return;
            _isSelectingCrop = false;
            CropCanvas.ReleaseMouseCapture();
            var point = ClampToRect(e.GetPosition(CropCanvas), GetDisplayedVideoRect());
            _draftCropBounds = new System.Windows.Rect(_cropDragStart, point);
            if (_draftCropBounds.Width < 8 || _draftCropBounds.Height < 8)
                ShowCurrentCropInCanvas();
            else
                DrawCropRectangle();
        }

        private static System.Windows.Point ClampToRect(System.Windows.Point point, System.Windows.Rect rect)
        {
            return new System.Windows.Point(
                Math.Clamp(point.X, rect.Left, rect.Right),
                Math.Clamp(point.Y, rect.Top, rect.Bottom));
        }

        private void DrawCropRectangle()
        {
            Canvas.SetLeft(CropRectangle, _draftCropBounds.Left);
            Canvas.SetTop(CropRectangle, _draftCropBounds.Top);
            CropRectangle.Width = Math.Max(0, _draftCropBounds.Width);
            CropRectangle.Height = Math.Max(0, _draftCropBounds.Height);
        }

        private void BtnResetCrop_Click(object sender, RoutedEventArgs e)
        {
            _draftCropBounds = GetDisplayedVideoRect();
            DrawCropRectangle();
        }

        private void BtnCancelCrop_Click(object sender, RoutedEventArgs e) => CropPopup.IsOpen = false;

        private void BtnConfirmCrop_Click(object sender, RoutedEventArgs e)
        {
            var videoRect = GetDisplayedVideoRect();
            if (videoRect.Width <= 0 || _draftCropBounds.Width < 8 || _draftCropBounds.Height < 8) return;
            _cropRegion = new CropRegion(
                Math.Clamp((_draftCropBounds.Left - videoRect.Left) / videoRect.Width, 0, 1),
                Math.Clamp((_draftCropBounds.Top - videoRect.Top) / videoRect.Height, 0, 1),
                Math.Clamp(_draftCropBounds.Width / videoRect.Width, 0, 1),
                Math.Clamp(_draftCropBounds.Height / videoRect.Height, 0, 1));
            CropPopup.IsOpen = false;
            TxtCropSummary.Text = _cropRegion.IsFull
                ? "全画面"
                : $"裁剪 {Math.Round(_cropRegion.Width * 100)}% × {Math.Round(_cropRegion.Height * 100)}%";
            TxtStatus.Text = _cropRegion.IsFull
                ? "已恢复全画面，分析时会发送完整关键帧。"
                : "裁剪区域已确认，分析时只会发送框选区域。";
        }

        private void UpdateTimelineVisuals()
        {
            if (_durationMs <= 0 || TimelineHost == null) return;
            var width = TimelineHost.ActualWidth;
            var left = width * InPointSlider.Value / _durationMs;
            var right = width * OutPointSlider.Value / _durationMs;
            SelectionOverlay.Width = Math.Max(2, right - left);
            Canvas.SetLeft(SelectionOverlay, left);
            Canvas.SetLeft(Playhead, width * PlaybackSlider.Value / _durationMs);
        }

        private async void BtnAnalyze_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_videoPath) || CmbVisionModel.SelectedItem is not AiModel model) return;

            SetBusy(true, "正在从选中片段提取关键帧…", true);
            try
            {
                var selectedItem = CmbFrameRate.SelectedItem as ComboBoxItem;
                var framesPerSecond = int.TryParse(selectedItem?.Tag?.ToString(), out var rate) ? rate : 4;
                var start = InPointSlider.Value;
                var end = OutPointSlider.Value;
                var crop = _cropRegion;
                var requestedFrameCount = CalculateAnalysisFrameCount(start, end, framesPerSecond);
                if (requestedFrameCount > MaxAnalysisFrames)
                {
                    throw new InvalidOperationException(
                        $"当前片段按 {framesPerSecond} 帧/秒会产生 {requestedFrameCount} 张关键帧，超过单次分析上限 {MaxAnalysisFrames} 张。请缩短片段或降低每秒抽帧数。");
                }

                TxtStatus.Text = $"正在按 {framesPerSecond} 帧/秒提取约 {requestedFrameCount} 张关键帧…";
                var frames = await Task.Run(() => ExtractAnalysisFrames(_videoPath, start, end, framesPerSecond, crop));
                if (frames.Count == 0) throw new InvalidOperationException("没有成功提取到关键帧。");

                var service = new LLMService();
                var uploadProgress = new Progress<(int Current, int Total)>(value =>
                {
                    TxtStatus.Text = $"正在上传关键帧到腾讯 COS… {value.Current}/{value.Total}";
                });
                var images = await service.UploadVideoAnalysisFramesAsync(frames, uploadProgress);

                TxtStatus.Text = $"已上传 {images.Count} 帧，正在分析动效并编写 HTML…";
                var ratio = (CmbAspectRatio.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "跟随原视频";
                TxtAnalysisResult.Clear();
                TxtHtmlResult.Clear();
                BtnCopyHtml.IsEnabled = false;
                BtnSaveHtml.IsEnabled = false;
                ResultTabs.SelectedIndex = 0;

                var requirements = $"关键帧已按用户框选区域裁剪；请把收到的画面边界视为最终舞台边界。\n{TxtExtraRequirements.Text}".Trim();
                // DeepSeek 往往会连续返回大量很小的分片。后台消费网络流，并限制界面刷新频率，
                // 避免每个 token 都触发 TextBox 全量排版而卡住 UI 线程。
                var response = await Task.Run(async () =>
                {
                    var responseBuilder = new StringBuilder();
                    var renderTimer = Stopwatch.StartNew();

                    await foreach (var chunk in service.AnalyzeVideoRecreationStreamAsync(
                        model, images, start, end, ratio, requirements))
                    {
                        responseBuilder.Append(chunk);
                        if (renderTimer.ElapsedMilliseconds < 180) continue;

                        var snapshot = responseBuilder.ToString();
                        await Dispatcher.InvokeAsync(() =>
                        {
                            UpdateStreamingResult(snapshot);
                            TxtStatus.Text = $"AI 正在流式输出… 已接收 {snapshot.Length:N0} 个字符";
                        }, System.Windows.Threading.DispatcherPriority.Background);
                        renderTimer.Restart();
                    }

                    var finalResponse = responseBuilder.ToString();
                    await Dispatcher.InvokeAsync(() =>
                    {
                        UpdateStreamingResult(finalResponse);
                        TxtStatus.Text = $"AI 正在流式输出… 已接收 {finalResponse.Length:N0} 个字符";
                    }, System.Windows.Threading.DispatcherPriority.Background);
                    return finalResponse;
                });

                var (analysis, html) = ParseAiResponse(response);
                TxtAnalysisResult.Text = string.IsNullOrWhiteSpace(analysis) ? "模型未返回视觉拆解。" : analysis.Trim();
                TxtHtmlResult.Text = string.IsNullOrWhiteSpace(html) ? response.Trim() : html.Trim();
                BtnCopyHtml.IsEnabled = !string.IsNullOrWhiteSpace(TxtHtmlResult.Text);
                BtnSaveHtml.IsEnabled = BtnCopyHtml.IsEnabled;
                ResultTabs.SelectedIndex = 0;
                TxtStatus.Text = "复刻完成。建议先检查视觉拆解，再保存 HTML 并替换素材占位区域。";
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"分析失败：{ex.Message}";
                MessageBox.Show(ex.Message, "AI 分析失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                SetBusy(false, TxtStatus.Text, true);
            }
        }

        private void UpdateStreamingResult(string response)
        {
            var analysisStart = response.IndexOf("<ANALYSIS>", StringComparison.OrdinalIgnoreCase);
            var analysisEnd = response.IndexOf("</ANALYSIS>", StringComparison.OrdinalIgnoreCase);
            var htmlStart = response.IndexOf("<HTML>", StringComparison.OrdinalIgnoreCase);
            var htmlEnd = response.IndexOf("</HTML>", StringComparison.OrdinalIgnoreCase);

            if (analysisStart >= 0)
            {
                var contentStart = analysisStart + "<ANALYSIS>".Length;
                var contentEnd = analysisEnd > contentStart
                    ? analysisEnd
                    : htmlStart > contentStart ? htmlStart : response.Length;
                TxtAnalysisResult.Text = response[contentStart..contentEnd].TrimStart();
                TxtAnalysisResult.ScrollToEnd();
            }
            else if (response.Length > 16)
            {
                // 兼容不遵守分段标签的 OpenAI 兼容模型。
                TxtAnalysisResult.Text = response;
                TxtAnalysisResult.ScrollToEnd();
            }

            if (htmlStart >= 0)
            {
                var contentStart = htmlStart + "<HTML>".Length;
                var contentEnd = htmlEnd > contentStart ? htmlEnd : response.Length;
                TxtHtmlResult.Text = response[contentStart..contentEnd]
                    .Replace("```html", string.Empty, StringComparison.OrdinalIgnoreCase)
                    .Replace("```", string.Empty)
                    .TrimStart();
                TxtHtmlResult.ScrollToEnd();
            }
        }

        private static int CalculateAnalysisFrameCount(double startMs, double endMs, int framesPerSecond)
        {
            var durationMs = Math.Max(0, endMs - startMs);
            return Math.Max(1, (int)Math.Ceiling(durationMs / 1000d * Math.Max(1, framesPerSecond)));
        }

        private static List<VideoAnalysisFrame> ExtractAnalysisFrames(string path, double startMs, double endMs, int framesPerSecond, CropRegion crop)
        {
            using var capture = new VideoCapture(path);
            if (!capture.IsOpened()) throw new InvalidOperationException("无法重新打开视频进行抽帧。");
            var result = new List<VideoAnalysisFrame>();
            var count = CalculateAnalysisFrameCount(startMs, endMs, framesPerSecond);
            var intervalMs = 1000d / Math.Max(1, framesPerSecond);
            for (var i = 0; i < count; i++)
            {
                // 从入点开始按固定间隔取帧，保持用户选择的“每秒帧数”语义。
                var time = Math.Min(startMs + i * intervalMs, Math.Max(startMs, endMs - 1));
                capture.Set(VideoCaptureProperties.PosMsec, time);
                using var frame = new Mat();
                if (!capture.Read(frame) || frame.Empty()) continue;
                var cropX = Math.Clamp((int)Math.Round(crop.X * frame.Width), 0, Math.Max(0, frame.Width - 1));
                var cropY = Math.Clamp((int)Math.Round(crop.Y * frame.Height), 0, Math.Max(0, frame.Height - 1));
                var cropWidth = Math.Clamp((int)Math.Round(crop.Width * frame.Width), 1, frame.Width - cropX);
                var cropHeight = Math.Clamp((int)Math.Round(crop.Height * frame.Height), 1, frame.Height - cropY);
                using var cropped = new Mat(frame, new OpenCvSharp.Rect(cropX, cropY, cropWidth, cropHeight));
                using var resized = ResizeForUpload(cropped, 1280);
                Cv2.ImEncode(".jpg", resized, out var bytes, new[] { (int)ImwriteFlags.JpegQuality, 84 });
                result.Add(new VideoAnalysisFrame(time, bytes));
            }
            return result;
        }

        private static (string Analysis, string Html) ParseAiResponse(string response)
        {
            var analysisStart = response.IndexOf("<ANALYSIS>", StringComparison.OrdinalIgnoreCase);
            var analysisEnd = response.IndexOf("</ANALYSIS>", StringComparison.OrdinalIgnoreCase);
            var htmlStart = response.IndexOf("<HTML>", StringComparison.OrdinalIgnoreCase);
            var htmlEnd = response.IndexOf("</HTML>", StringComparison.OrdinalIgnoreCase);
            var analysis = analysisStart >= 0 && analysisEnd > analysisStart
                ? response[(analysisStart + 10)..analysisEnd]
                : response;
            var html = htmlStart >= 0 && htmlEnd > htmlStart
                ? response[(htmlStart + 6)..htmlEnd]
                : string.Empty;
            html = html.Replace("```html", string.Empty, StringComparison.OrdinalIgnoreCase).Replace("```", string.Empty);
            return (analysis, html);
        }

        private void BtnCopyHtml_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(TxtHtmlResult.Text);
            TxtStatus.Text = "HTML 已复制到剪贴板。";
        }

        private void BtnSaveHtml_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Title = "保存复刻 HTML",
                Filter = "HTML 文件|*.html",
                FileName = $"video-recreation-{DateTime.Now:yyyyMMdd-HHmmss}.html"
            };
            if (dialog.ShowDialog() != true) return;
            File.WriteAllText(dialog.FileName, TxtHtmlResult.Text, new System.Text.UTF8Encoding(false));
            TxtStatus.Text = $"已保存：{dialog.FileName}";
        }

        private void SetBusy(bool busy, string status, bool analyzing)
        {
            BtnOpenVideo.IsEnabled = !busy;
            BtnAnalyze.IsEnabled = !busy && !string.IsNullOrWhiteSpace(_videoPath) && CmbVisionModel.Items.Count > 0;
            AnalysisProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            TxtAnalyzeButton.Text = busy && analyzing ? "正在分析与生成…" : "分析并生成 HTML";
            TxtStatus.Text = status;
        }

        public void PauseVideo()
        {
            if (_mediaPlayer.IsPlaying) _mediaPlayer.Pause();
            CropPopup.IsOpen = false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _mediaPlayer.Stop();
            _mediaPlayer.Dispose();
            _libVlc.Dispose();
        }
    }
}
