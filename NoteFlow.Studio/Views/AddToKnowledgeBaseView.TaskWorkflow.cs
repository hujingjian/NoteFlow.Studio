using NoteFlow.Studio.Infrastructure.Logging;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Text;
using NoteFlow.Studio.Model.Config;
using NoteFlow.Studio.Repository.Config;
using LibVLCSharp.Shared;
using LibVLCSharp.WPF;
using NoteFlow.Studio.Model;
using NoteFlow.Studio.Repository;


namespace NoteFlow.Studio.Views
{
    public partial class AddToKnowledgeBaseView
    {
        private ProcessingRecordRepository _recordRepository = new ProcessingRecordRepository();

        private async Task UpdateRecordStatus(string videoId, string status)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var record = _recordRepository.GetById(videoId);
                if (record != null)
                {
                    record.Status = status;
                    _recordRepository.AddOrUpdate(record);
                    
                    // 通知主窗口刷新列表
                    if (Application.Current.MainWindow is MainWindow mw)
                    {
                        mw.RefreshProcessingRecords();
                    }
                }
            });
        }

        private async void BtnReExtract_Click(object sender, RoutedEventArgs e)
        {
            var methodStr = (CmbExtractMethod.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
            if (methodStr != "字幕提取")
            {
                if (methodStr == "字幕区域OCR" && (CropRectangle == null || CropRectangle.Width == 0 || CropRectangle.Height == 0))
                {
                    MessageBox.Show("还未框选字幕区域");
                    return;
                }
                if (string.IsNullOrEmpty(_currentVideoUrl) || !File.Exists(_currentVideoUrl))
                {
                    MessageBox.Show("请先下载完视频后再点击重试提取");
                    return;
                }
            }
            if (!_isTaskRunning)
            {
                _isTaskRunning = true;
            }

            _globalCts = new System.Threading.CancellationTokenSource();
            BtnReExtract.IsEnabled = false; // Disable while extracting
            TxtOcrResult.Text = "";
            TxtPureOcrResult.Text = "";
            try
            {
                await ExtractTranscription(_currentVideoUrl);
            }
            finally
            {
                BtnReExtract.IsEnabled = true;
            }
        }

        private async void BtnToggleTask_Click(object sender, RoutedEventArgs e)
        {
            if (!_isTaskRunning)
            {
                var methodStr = (CmbExtractMethod.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
                bool skipExtraction = false;
                if (methodStr == "字幕区域OCR" && (CropRectangle == null || CropRectangle.Width == 0 || CropRectangle.Height == 0))
                {
                    if (!string.IsNullOrEmpty(_currentVideoUrl) && File.Exists(_currentVideoUrl))
                    {
                        MessageBox.Show("还未框选字幕区域");
                        return;
                    }
                    else
                    {
                        var result = MessageBox.Show("还未框选字幕区域，是否直接开始，可以下载视频完成后再框选", "提示", MessageBoxButton.YesNo);
                        if (result != MessageBoxResult.Yes) return;
                        skipExtraction = true;
                    }
                }

                _isTaskRunning = true;
                _globalCts = new System.Threading.CancellationTokenSource();
                BtnToggleTask.Content = "取消任务";
                BtnToggleTask.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#EF4444"));

                TxtOcrResult.Text = "";
                TxtPureOcrResult.Text = "";

                try
                {
                    await UpdateRecordStatus(_currentVideoId, "处理中-视频下载");

                    if (_currentVideoPlatform == "本地视频" && File.Exists(_currentVideoUrl))
                    {
                        TxtVideoStatus.Text = "本地视频准备就绪。";
                        TxtStep1Desc.Text = "已下载";
                        ProgStep1.Value = 100;
                        IconStep1.Kind = MaterialDesignThemes.Wpf.PackIconKind.CheckCircle;
                        IconStep1.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 197, 94));
                        await UpdateRecordStatus(_currentVideoId, "处理中-视频已就绪");
                    }
                    else if (_currentVideoPlatform == "YouTube")
                    {
                        if (methodStr != "字幕提取")
                        {
                            await DownloadVideoWithYtDlp();
                        }
                    }
                    else
                    {
                        // B站、抖音等其他平台
                        await SimulateDownloadAndShowVideo();
                    }

                    await UpdateRecordStatus(_currentVideoId, "处理中-文案提取");
                    BtnReExtract.IsEnabled = false;
                    try
                    {
                        if (!skipExtraction) { await ExtractTranscription(_currentVideoUrl); }
                        else { ProgStep2.Visibility = Visibility.Visible; TxtStep2Desc.Text = "请完成框选后再提取"; }
                    }
                    finally
                    {
                        BtnReExtract.IsEnabled = true;
                        if (_isTaskRunning)
                        {
                            _isTaskRunning = false;
                            BtnToggleTask.Content = "开始任务";
                            BtnToggleTask.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#414DE5"));
                        }
                    }
                }
                catch (Exception ex)
                {
                    await UpdateRecordStatus(_currentVideoId, "失败");
                    AppLogger.Error("任务执行失败", ex);
                }
            }
            else
            {
                _isTaskRunning = false;
                _globalCts?.Cancel();
                BtnToggleTask.Content = "开始任务";
                BtnToggleTask.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#414DE5"));
                TxtVideoStatus.Text = "任务已取消。";
            }
        }

        private async Task DownloadVideoWithYtDlp()
        {
            try
            {
                var configRepo = new ConfigRepository();
                string defaultDownloadDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Downloads");
                string downloadDir = configRepo.GetConfig(AppConfigKeys.VideoDownloadPath, defaultDownloadDir);
                if (string.IsNullOrWhiteSpace(downloadDir)) downloadDir = defaultDownloadDir;

                if (!Directory.Exists(downloadDir)) Directory.CreateDirectory(downloadDir);

                string fileName = $"{_currentVideoId}.mp4";
                string localPath = Path.Combine(downloadDir, fileName);

                TxtVideoStatus.Text = "正在下载 YouTube 视频...";
                TxtStep1Desc.Text = "准备下载...";
                ProgStep1.Value = 0;

                var youtubeService = new Service.VideoParsers.YouTubeVideoParserService();
                var progress = new Progress<double>(p =>
                {
                    ProgStep1.Value = p * 100;
                    TxtStep1Desc.Text = $"下载中 {p:P0}";
                });

                await youtubeService.DownloadVideoAsync(_currentVideoId, localPath, _cookies, progress);

                _currentVideoUrl = localPath;
                TxtStep1Desc.Text = "已下载";
                ProgStep1.Value = 100;
                IconStep1.Kind = MaterialDesignThemes.Wpf.PackIconKind.CheckCircle;
                IconStep1.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 197, 94));
                TxtVideoStatus.Text = "视频准备就绪。";
                await UpdateRecordStatus(_currentVideoId, "处理中-视频下载完成");

                VideoPlayer.Visibility = Visibility.Visible;
                PanelVideoControls.Visibility = Visibility.Visible;
                var localMedia = new Media(_libVLC, new Uri(_currentVideoUrl));
                _mediaPlayer.Play(localMedia);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"YouTube 视频下载失败: {ex.Message}", ex);
                TxtVideoStatus.Text = $"YouTube 视频下载失败: {ex.Message}";
                TxtStep1Desc.Text = "下载失败";
                await UpdateRecordStatus(_currentVideoId, "失败");
            }
        }


        private async Task SimulateDownloadAndShowVideo()
        {
            try
            {
                var configRepo = new ConfigRepository();
                string defaultDownloadDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Downloads");
                string downloadDir = configRepo.GetConfig(AppConfigKeys.VideoDownloadPath, defaultDownloadDir);
                if (string.IsNullOrWhiteSpace(downloadDir)) downloadDir = defaultDownloadDir;

                if (!Directory.Exists(downloadDir)) Directory.CreateDirectory(downloadDir);
                
                string fileName = string.IsNullOrEmpty(_currentVideoId) ? "video_" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".mp4" : $"{_currentVideoId}.mp4";
                string localPath = Path.Combine(downloadDir, fileName);

                if (File.Exists(localPath))
                {
                    _currentVideoUrl = localPath;
                    TxtStep1Desc.Text = "已下载";
                    ProgStep1.Value = 100;
                    IconStep1.Kind = MaterialDesignThemes.Wpf.PackIconKind.CheckCircle;
                    IconStep1.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 197, 94)); // #22C55E
                    TxtVideoStatus.Text = "视频准备就绪。";
                    await UpdateRecordStatus(_currentVideoId, "处理中-视频下载完成");
                    
                    bool isAlreadyPlayingLocal = _mediaPlayer.Media != null &&
                        (Uri.UnescapeDataString(_mediaPlayer.Media.Mrl) == new Uri(_currentVideoUrl).ToString() ||
                         Uri.UnescapeDataString(_mediaPlayer.Media.Mrl) == new Uri(_onlineVideoUrl).ToString());

                    if (!isAlreadyPlayingLocal)
                    {
                        VideoPlayer.Visibility = Visibility.Visible;
                        PanelVideoControls.Visibility = Visibility.Visible;
                        var localMedia = new Media(_libVLC, new Uri(_currentVideoUrl));
                        _mediaPlayer.Play(localMedia);
                    }
                    //else
                    //{
                    //    VideoPlayer.Visibility = Visibility.Visible;
                    //    PanelVideoControls.Visibility = Visibility.Visible;
                    //}
                    return;
                }

                TxtVideoStatus.Text = "正在下载视频...";
                TxtStep1Desc.Text = "准备下载...";
                ProgStep1.Value = 0;
                //VideoPlayer.Visibility = Visibility.Collapsed;
                //PanelVideoControls.Visibility = Visibility.Collapsed;

                string targetUrl = string.IsNullOrEmpty(_onlineVideoUrl) ? _currentVideoUrl : _onlineVideoUrl;

                using (var httpClient = new System.Net.Http.HttpClient())
                {
                    if (targetUrl.Contains("bilibili") || targetUrl.Contains("bilivideo") || targetUrl.Contains("bilivideo.cn"))
                    {
                        httpClient.DefaultRequestHeaders.Referrer = new Uri("https://www.bilibili.com/");
                        httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120.0.0.0 Safari/537.36");
                    }

                    using (var response = await httpClient.GetAsync(targetUrl, System.Net.Http.HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode();
                        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                        var canReportProgress = totalBytes != -1;

                        using (var contentStream = await response.Content.ReadAsStreamAsync())
                        using (var fileStream = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                        {
                            var totalRead = 0L;
                            var buffer = new byte[8192];
                            var isMoreToRead = true;

                            while (isMoreToRead)
                            {
                                if (_globalCts != null && _globalCts.IsCancellationRequested)
                                {
                                    TxtStep1Desc.Text = "已取消下载";
                                    return;
                                }

                                var read = await contentStream.ReadAsync(buffer, 0, buffer.Length);
                                if (read == 0)
                                {
                                    isMoreToRead = false;
                                }
                                else
                                {
                                    await fileStream.WriteAsync(buffer, 0, read);
                                    totalRead += read;

                                    if (canReportProgress)
                                    {
                                        double progress = (double)totalRead / totalBytes * 100;
                                        ProgStep1.Value = progress;
                                        TxtStep1Desc.Text = $"下载中 {(totalRead / 1048576.0):F2}MB / {(totalBytes / 1048576.0):F2}MB";
                                    }
                                    else
                                    {
                                        TxtStep1Desc.Text = $"下载中 {(totalRead / 1048576.0):F2}MB";
                                    }
                                }
                            }
                        }
                    }
                }
                TxtStep1Desc.Text = "下载完成";
                ProgStep1.Value = 100;
                IconStep1.Kind = MaterialDesignThemes.Wpf.PackIconKind.CheckCircle;
                IconStep1.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 197, 94)); // #22C55E
                await UpdateRecordStatus(_currentVideoId, "处理中-视频下载完成");

                TxtVideoStatus.Text = "视频准备就绪。";
                _currentVideoUrl = localPath;
                
                //bool isAlreadyPlayingAfterDownload = _mediaPlayer.Media != null &&
                //    (Uri.UnescapeDataString(_mediaPlayer.Media.Mrl) == new Uri(_currentVideoUrl).ToString() ||
                //     Uri.UnescapeDataString(_mediaPlayer.Media.Mrl) == new Uri(_onlineVideoUrl).ToString());

                //VideoPlayer.Visibility = Visibility.Visible;
                //PanelVideoControls.Visibility = Visibility.Visible;
                
                //// Allow UI thread a tiny moment to flush layout so the HWND can map BEFORE playing
                //await Task.Delay(50);
                
                //if (!isAlreadyPlayingAfterDownload)
                //{
                //    var media = new Media(_libVLC, new Uri(_currentVideoUrl));
                //    _mediaPlayer.Play(media);
                //}
            }
            catch (Exception ex)
            {
                AppLogger.Error($"下载失败: {ex.Message}", ex);
                TxtVideoStatus.Text = $"下载失败: {ex.Message}";
                TxtStep1Desc.Text = "下载失败";
                await UpdateRecordStatus(_currentVideoId, "失败");
            }
        }



        private void CmbExtractMethod_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PanelWhisperConfig == null || PanelSubtitleConfig == null || PanelOcrConfig == null) return;

            PanelWhisperConfig.Visibility = Visibility.Collapsed;
            PanelSubtitleConfig.Visibility = Visibility.Collapsed;
            PanelOcrConfig.Visibility = Visibility.Collapsed;
            PanelYtSubtitleLanguage.Visibility = Visibility.Collapsed;

            if (CmbExtractMethod.SelectedItem is ComboBoxItem item)
            {
                string method = item.Content.ToString() ?? "";
                if (method == "whisper语音识别")
                {
                    PanelWhisperConfig.Visibility = Visibility.Visible;
                }
                else if (method == "字幕区域OCR")
                {
                    PanelSubtitleConfig.Visibility = Visibility.Visible;
                }
                else if (method == "截图识别")
                {
                    PanelOcrConfig.Visibility = Visibility.Visible;
                }

                if (method == "字幕提取")
                {
                    PanelYtSubtitleLanguage.Visibility = Visibility.Visible;
                    TxtStep1Desc.Text = "跳过下载";
                    ProgStep1.Value = 100;
                    IconStep1.Kind = MaterialDesignThemes.Wpf.PackIconKind.CheckCircle;
                    IconStep1.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 197, 94)); // Green
                }
                else
                {
                    if (TxtVideoStatus != null && TxtVideoStatus.Text.Contains("未下载"))
                    {
                        TxtStep1Desc.Text = "等待下载";
                        ProgStep1.Value = 0;
                        IconStep1.Kind = MaterialDesignThemes.Wpf.PackIconKind.ClockOutline;
                        IconStep1.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(139, 143, 154)); // Gray
                    }
                }
            }
        }

        StringBuilder ocrResultBuilder = new StringBuilder();
        StringBuilder pureOcrResultBuilder = new StringBuilder();

        private async Task ExtractTranscription(string localVideoPath)
        {
            ProgStep2.Visibility = Visibility.Visible;
            ProgStep2.Value = 0;
            TxtStep2Desc.Text = "准备提取...";

            string method = (CmbExtractMethod.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
            
            ocrResultBuilder = new StringBuilder();
            pureOcrResultBuilder = new StringBuilder();
            var lastUpdate = DateTime.MinValue;
            const int UI_UPDATE_INTERVAL_SECONDS = 2;

            Action<string, string> updateResults = (timedText, pureText) =>
            {
                ocrResultBuilder.AppendLine(timedText);
                pureOcrResultBuilder.AppendLine(pureText);

                if ((DateTime.Now - lastUpdate).TotalSeconds >= UI_UPDATE_INTERVAL_SECONDS)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        TxtOcrResult.Text = ocrResultBuilder.ToString();
                        TxtOcrResult.ScrollToEnd();
                        
                        TxtPureOcrResult.Text = pureOcrResultBuilder.ToString();
                        TxtPureOcrResult.ScrollToEnd();
                    });
                    lastUpdate = DateTime.Now;
                }
            };
            
            if (method == "whisper语音识别")
            {
                TxtStep2Desc.Text = "FFmpeg提取音频中...";
                ProgStep2.Value = 10;
                
                try
                {
                    var whisperService = new NoteFlow.Studio.Service.Providers.WhisperService();
                    
                    var getModel = CmbWhisperModel.SelectedItem as ComboBoxItem;
                    if (getModel == null || getModel.Tag == null)
                    {
                        MessageBox.Show("请先选择并下载Whisper识别模型！");
                        return;
                    }
                    
                    string modelName = getModel.Tag.ToString();
                    string modelPath = Path.Combine(GetToolDownloadPath(), "whisper", modelName);
                    if (!File.Exists(modelPath))
                    {
                        MessageBox.Show("所选模型尚未下载，请先下载模型！");
                        return;
                    }

                    string language = "auto";
                    if (CmbWhisperLanguage.SelectedItem is ComboBoxItem langItem && langItem.Tag != null)
                    {
                        language = langItem.Tag.ToString();
                    }
                                        
                    using var cts = new CancellationTokenSource();

                    // Step 1: extract audio to wav
                    var wavFile = await whisperService.ExtractAudioToWavAsync(localVideoPath, GetToolDownloadPath(), cts.Token);
                    ProgStep2.Value = 30;

                    if (_globalCts != null && _globalCts.IsCancellationRequested) 
                    {
                        TxtStep2Desc.Text = "已取消";
                        if (File.Exists(wavFile)) File.Delete(wavFile);
                        return;
                    }

                    TxtStep2Desc.Text = "Whisper推断中...";
                    
                    // Step 2: transcribe
                    await Task.Run(async () => 
                    {
                        await whisperService.TranscribeAudioAsync(wavFile, modelPath, language, (timeRange, text) =>
                        {
                            if (_globalCts != null && _globalCts.IsCancellationRequested) cts.Cancel();

                            updateResults($"{timeRange} {text}", text);
                            
                        }, cts.Token);
                    });

                    // Cleanup temp audio
                    if (File.Exists(wavFile)) File.Delete(wavFile);
                    
                    if (_globalCts != null && _globalCts.IsCancellationRequested) 
                    {
                        TxtStep2Desc.Text = "已取消";
                        return;
                    }
                }
                catch (Exception ex)
                {
                    if (ex is OperationCanceledException || (ex.InnerException != null && ex.InnerException is OperationCanceledException))
                    {
                        TxtStep2Desc.Text = "已取消";
                    }
                    else
                    {
                        AppLogger.Error($"文案提取异常: {ex.Message}", ex);
                        TxtStep2Desc.Text = "Whisper提取失败";
                        MessageBox.Show($"文案提取异常: {ex.Message}");
                        await UpdateRecordStatus(_currentVideoId, "失败");
                    }
                    return;
                }
            }
            else if (method == "字幕区域OCR")
            {
                TxtStep2Desc.Text = "读取截图区域...";
                if(_globalCts != null && _globalCts.IsCancellationRequested) return;

                if (CropRectangle == null || CropRectangle.Width == 0 || CropRectangle.Height == 0)
                {
                    TxtStep2Desc.Text = "未能提取: 未框选区域";
                    ProgStep2.Visibility = Visibility.Collapsed;
                    MessageBox.Show("请先框选字幕所在的区域");
                    return;
                }

                try
                {
                    string ffmpegPath = System.IO.Path.Combine(GetToolDownloadPath(), "ffmpeg");
                    var ocrService = new NoteFlow.Studio.Service.Providers.SubtitleOcrService(ffmpegPath);
                    
                    VideoContainer.UpdateLayout(); // 强制更新布局，避免由控件可见性切换引起的延迟导致长宽获取不准
                    
                    double visualW = VideoContainer.ActualWidth;
                    double visualH = VideoContainer.ActualHeight;

                    // 以用户框选范围为准
                    double cropX = Canvas.GetLeft(CropRectangle);
                    if (double.IsNaN(cropX)) cropX = 0;
                    double cropW = CropRectangle.Width;
                    
                    double cropY = Canvas.GetTop(CropRectangle);
                    if (double.IsNaN(cropY)) cropY = 0;
                    
                    double cropH = CropRectangle.Height;

                    TxtStep2Desc.Text = "准备 OCR, 这可能需要一些时间...";
                    
                    using var cts = new System.Threading.CancellationTokenSource();
                    
                    var progress = new Progress<int>(p => 
                    {
                        if (_globalCts != null && _globalCts.IsCancellationRequested) cts.Cancel();
                        
                        ProgStep2.Value = p;
                        TxtStep2Desc.Text = $"OCR 识别中... {p}%";
                    });

                    // 移除清空逻辑，以免覆盖单帧截图识别的内容

                    Action<NoteFlow.Studio.Service.Providers.SubtitleItem> onSubtitleParam = item => 
                    {
                        updateResults(item.ToString(), item.Text);
                    };

                    var resultText = await Task.Run(async () => 
                    {
                        return await ocrService.ExtractSubtitlesAsync(
                            localVideoPath, 
                            cropX, cropY, cropW, cropH, 
                            visualW, visualH, 
                            progress, cts.Token, onSubtitleParam);
                    });

                    // _ = resultText; // we already appended it through onSubtitleParam, do not overwrite TxtOcrResult.
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"文案提取异常: {ex.Message}", ex);
                    TxtStep2Desc.Text = "提取失败";
                    MessageBox.Show($"文案提取异常: {ex.Message}");
                    await UpdateRecordStatus(_currentVideoId, "失败");
                    return;
                }
            }            
            else if (method == "字幕提取")
            {
                TxtStep2Desc.Text = "加载字幕...";
                ProgStep2.Value = 50;

                try
                {
                    var youtubeService = new Service.VideoParsers.YouTubeVideoParserService();
                    var selectedLanguageItem = CmbYtSubtitleLanguage.SelectedItem as ComboBoxItem;
                    if (selectedLanguageItem?.Tag is YoutubeExplode.Videos.ClosedCaptions.Language lang)
                    {
                        var youtubeSrt = await youtubeService.GetCaptionAsync(_currentVideoId, lang, _cookies);
                        updateResults(youtubeSrt.Item1.ToString(), youtubeSrt.Item2.ToString());
                    }
                    else
                    {
                        MessageBox.Show("请选择一个字幕语言。");
                        TxtStep2Desc.Text = "提取失败";
                        return;
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"YouTube字幕提取异常: {ex.Message}", ex);
                    TxtStep2Desc.Text = "字幕提取失败";
                    MessageBox.Show($"YouTube字幕提取异常: {ex.Message}");
                    await UpdateRecordStatus(_currentVideoId, "失败");
                    return;
                }
            }
            // Final UI update to flush any remaining text
            Application.Current.Dispatcher.Invoke(() =>
            {
                TxtOcrResult.Text = ocrResultBuilder.ToString();
                TxtOcrResult.ScrollToEnd();

                TxtPureOcrResult.Text = pureOcrResultBuilder.ToString();
                TxtPureOcrResult.ScrollToEnd();
            });

            TxtStep2Desc.Text = "完成";
            ProgStep2.Value = 100;
            IconStep2.Kind = MaterialDesignThemes.Wpf.PackIconKind.CheckCircle;
            IconStep2.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 197, 94)); // #22C55E
            await UpdateRecordStatus(_currentVideoId, "处理中-文案提取完成");
        }

    }
}
