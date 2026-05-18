using CefSharp;
using CefSharp.Handler;
using CefSharp.Wpf;
using NoteFlow.Studio.CefSharpHandlers;
using NoteFlow.Studio.Service;
using NoteFlow.Studio.Service.Interfaces;
using NoteFlow.Studio.Service.Models;
using NoteFlow.Studio.Service.VideoParsers;
using NoteFlow.Studio.Infrastructure.Logging;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using NoteFlow.Studio.Model;
using NoteFlow.Studio.Repository;

namespace NoteFlow.Studio
{
    public class MockTaskItem : INotifyPropertyChanged
    {
        private double _progress;
        private string _status = string.Empty;

        public string TaskInfo { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        
        public double Progress 
        { 
            get => _progress; 
            set { _progress = value; OnPropertyChanged(nameof(Progress)); } 
        }
        
        public string Status 
        { 
            get => _status; 
            set { _status = value; OnPropertyChanged(nameof(Status)); } 
        }
        
        public string UpdateTime { get; set; } = string.Empty;
        public string Actions { get; set; } = string.Empty;

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class CustomLifeSpanHandler : LifeSpanHandler
    {
        private readonly Action<string> _openNewTabAction;

        public CustomLifeSpanHandler(Action<string> openNewTabAction)
        {
            _openNewTabAction = openNewTabAction;
        }

        protected override bool OnBeforePopup(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, string targetUrl, string targetFrameName, WindowOpenDisposition targetDisposition, bool userGesture, IPopupFeatures popupFeatures, IWindowInfo windowInfo, IBrowserSettings browserSettings, ref bool noJavascriptAccess, out IWebBrowser newBrowser)
        {
            newBrowser = null;
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                _openNewTabAction?.Invoke(targetUrl);
            });
            return true;
        }
    }

    public partial class MainWindow : Window
    {
        private int _tabCounter = 1;
        private VideoParserServiceFactory _videoParserFactory;
        private string _lastFetchedVideoId;
        private VideoInfo _currentVideoInfo;
        private MainViewModel _viewModel;
        private ProcessingRecordRepository _processingRecordRepository;
        
        private int _errorCount = 0;
        private int _warningCount = 0;

        private void UpdateStatusLog(NoteFlow.Studio.Infrastructure.Logging.LogEntry log)
        {
            if (log.Level == "ERROR") _errorCount++;
            else if (log.Level == "WARN") _warningCount++;

            if (_errorCount > 0)
            {
                IconStatusLight.Foreground = System.Windows.Media.Brushes.Red;
                TxtStatusLog.Text = $"存在异常: {_errorCount} 个" + (_warningCount > 0 ? $"，警告: {_warningCount} 个" : "");
            }
            else if (_warningCount > 0)
            {
                IconStatusLight.Foreground = System.Windows.Media.Brushes.Yellow;
                TxtStatusLog.Text = $"存在警告: {_warningCount} 个";
            }
            else
            {
                IconStatusLight.Foreground = System.Windows.Media.Brushes.LimeGreen;
                TxtStatusLog.Text = "系统运行正常";
            }
        }

        public void ResetStatusLog()
        {
            _errorCount = 0;
            _warningCount = 0;
            if (IconStatusLight != null) IconStatusLight.Foreground = System.Windows.Media.Brushes.LimeGreen;
            if (TxtStatusLog != null) TxtStatusLog.Text = "系统运行正常";
        }

        public void RefreshProcessingRecords()
        {
            _viewModel.LoadProcessingRecords();
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void BtnMaximize_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal;
            else WindowState = WindowState.Maximized;
        }
        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        public MainWindow()
        {
            NoteFlow.Studio.Infrastructure.Logging.AppLogger.OnLogReceived += (log) => 
            {
                Application.Current.Dispatcher.InvokeAsync(() => 
                {
                    NoteFlow.Studio.Infrastructure.Logging.AppLogger.AddMemoryLogFromUI(log);
                    UpdateStatusLog(log);
                });
            };

            InitializeComponent();
            _viewModel = new MainViewModel();
            this.DataContext = _viewModel;
            _processingRecordRepository = new ProcessingRecordRepository();
            _viewModel.LoadProcessingRecords();
            AddKnowledgeBasePanel.TaskCompleted += (s, e) => _viewModel.LoadProcessingRecords();

            LoadGlobalAiModels();
            this.Loaded += MainWindow_Loaded;

            var playwrightProvider = new NoteFlow.Studio.Service.Providers.PlaywrightProvider();
            _videoParserFactory = new VideoParserServiceFactory(new IVideoParserService[] {
                new DouyinVideoParserService(playwrightProvider),
                new BilibiliVideoParserService(playwrightProvider),
                new XiaoHongShuVideoParserService(playwrightProvider),
                new YouTubeVideoParserService()
            });

            AddNewBrowserTab("首页", "https://www.douyin.com");

            BtnDouyin.Click += (s, e) => NavigateInCurrentTab("https://www.douyin.com");
            BtnBilibili.Click += (s, e) => NavigateInCurrentTab("https://www.bilibili.com");
            BtnYoutube.Click += (s, e) => NavigateInCurrentTab("https://www.youtube.com");
            //BtnDownloadVideo.Click += BtnDownloadVideo_Click;
        }

        public void LoadGlobalAiModels()
        {
            var repo = new NoteFlow.Studio.Repository.Config.AiModelRepository();
            var activeModels = repo.GetAll().Where(m => m.IsEnabled).ToList();
            if (CmbGlobalModel != null) 
            {
                CmbGlobalModel.ItemsSource = activeModels;
                if (activeModels.Count > 0) CmbGlobalModel.SelectedIndex = 0;
            }
        }

        public string GetCurrentBrowserUrl()
        {
            foreach (UIElement child in BrowserContainer.Children)
            {
                if (child is ChromiumWebBrowser browser && browser.Visibility == Visibility.Visible)
                {
                    return browser.Address;
                }
            }
            return string.Empty;
        }

        private void BtnAddToRepo_Click(object sender, RoutedEventArgs e)
        {
            if (_currentVideoInfo == null) return;

            var record = new ProcessingRecord
            {
                VideoId = _currentVideoInfo.VideoId,
                Title = _currentVideoInfo.Title,
                Description = _currentVideoInfo.Description,
                VideoUrl = _currentVideoInfo.PageUrl,
                CoverUrl = _currentVideoInfo.CoverUrl,
                ProcessingStartTime = DateTime.Now,
                Status = "等待中"
            };
            _processingRecordRepository.AddOrUpdate(record);
            _viewModel.LoadProcessingRecords();

            if (MenuAddKnowledgeBase != null)
                MenuAddKnowledgeBase.IsSelected = true;

            PauseVideo();

            if (AddKnowledgeBasePanel != null)
            {
                AddKnowledgeBasePanel.ResetVideoInfo();
                AddKnowledgeBasePanel.SetVideoInfo(_currentVideoInfo);
            }
        }
        private void PauseVideo()
        {
            // 暂停当前页面的视频播放
            foreach (UIElement child in BrowserContainer.Children)
            {
                if (child is ChromiumWebBrowser currentBrowser && currentBrowser.Visibility == Visibility.Visible)
                {
                    currentBrowser.GetMainFrame()?.ExecuteJavaScriptAsync("document.querySelectorAll('video').forEach(v => v.pause());");
                    break;
                }
            }
        }

        private void BtnNewTab_Click(object sender, RoutedEventArgs e)
        {
            AddNewBrowserTab($"新标签页 {_tabCounter++}", "https://www.douyin.com");
        }

        private void AddNewBrowserTab(string title, string url)
        {
            var browser = new ChromiumWebBrowser { Address = url, Background = Brushes.Transparent };

            browser.LifeSpanHandler = new CustomLifeSpanHandler(targetUrl => 
            {
                AddNewBrowserTab("...", targetUrl);
            });

            browser.AddressChanged += (s, args) =>
            {
                var address = args.NewValue as string;
                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    AddressTextBox.Text = address;
                    
                    string platform = null;
                    if (address != null)
                    {
                        if (address.Contains("douyin.com")) platform = "douyin";
                        else if (address.Contains("bilibili.com")) platform = "bilibili";
                        else if (address.Contains("youtube.com") || address.Contains("youtu.be")) platform = "youtube";
                        else if (address.Contains("xiaohongshu.com")) platform = "xiaohongshu";
                    }

                    if (platform != null)
                    {
                        var videoId = ExtractVideoId(address, platform);
                        if (!string.IsNullOrEmpty(videoId))
                        {
                            //if (videoId != _lastFetchedVideoId)
                            {
                                _lastFetchedVideoId = videoId;
                                FetchVideoDetailInBackground(videoId, platform, address);
                            }
                        }
                        else
                        {
                            ResetVideoInfoUI("请进入详情页");
                        }
                    }
                });
            };

            var tabTitleText = new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };

            browser.FrameLoadEnd += (sender, args) =>
            {
                if (args.Frame.IsMain)
                {
                    Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        browser.ZoomLevel = -1.5; 
                    });
                }
            };

            browser.TitleChanged += (sender, args) =>
            {
                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var newTitle = args.NewValue as string;
                    if (!string.IsNullOrWhiteSpace(newTitle))
                    {
                        tabTitleText.Text = newTitle.Length > 4 ? newTitle.Substring(0, 4) + "..." : newTitle;
                    }
                });
            };

            var tabHeaderContent = new StackPanel { Orientation = Orientation.Horizontal };
            tabHeaderContent.Children.Add(tabTitleText);

            Button tabBtn = null;

            // 只有非主Tab才添加关闭按钮 (因为界面上默认已有1个 BtnNewTab，所以此时数量>1才是新Tab)
            if (TabHeaderPanel.Children.Count > 1)
            {
                var closeButton = new Button
                {
                    Content = "✕",
                    Background = Brushes.Transparent,
                    Foreground = Brushes.Gray,
                    BorderThickness = new Thickness(0),
                    Margin = new Thickness(5, 0, 0, 0),
                    Cursor = Cursors.Hand,
                    Padding = new Thickness(2),
                    Height = 20,
                    Width = 20,
                    FontSize = 10
                };

                closeButton.Click += (s, e) =>
                {
                    e.Handled = true; // 防止触发tabBtn的Click
                    BrowserContainer.Children.Remove(browser);

                    if (tabBtn != null)
                    {
                        TabHeaderPanel.Children.Remove(tabBtn);
                    }
                    
                    // 强制清空地址，停止后台媒体音频播放
                    browser.Address = "about:blank";
                    browser.GetBrowser()?.StopLoad();
                    
                    browser.Dispose();

                    // 激活前一个
                    if (TabHeaderPanel.Children.Count > 1)
                    {
                        var lastTab = TabHeaderPanel.Children[TabHeaderPanel.Children.Count - 2] as Button;
                        if (lastTab != null && lastTab.Tag is ChromiumWebBrowser lastBrowser)
                        {
                            ActiveTab(lastTab, lastBrowser);
                        }
                    }
                };
                tabHeaderContent.Children.Add(closeButton);
            }

            tabBtn = new Button
            {
                Content = tabHeaderContent,
                Height = 35,
                Padding = new Thickness(20, 0, 10, 0),
                Background = (Brush)FindResource("AppCardBackground"),
                Foreground = (Brush)FindResource("AppTextForeground"),
                Margin = new Thickness(0, 0, 4, 0),
                Tag = browser 
            };
            
            tabBtn.Style = (Style)FindResource("MaterialDesignFlatButton");
            MaterialDesignThemes.Wpf.ButtonAssist.SetCornerRadius(tabBtn, new CornerRadius(10, 10, 0, 0));

            tabBtn.Click += (s, e) => ActiveTab(tabBtn, browser);

            TabHeaderPanel.Children.Insert(TabHeaderPanel.Children.Count - 1, tabBtn);
            BrowserContainer.Children.Add(browser);

            ActiveTab(tabBtn, browser);
        }

        private void ActiveTab(Button activeBtn, ChromiumWebBrowser activeBrowser)
        {
            foreach (var child in TabHeaderPanel.Children)
            {
                if (child is Button btn && btn != BtnNewTab)
                {
                    btn.Background = (Brush)FindResource("AppRootBackground"); 
                }
            }
            activeBtn.Background = (Brush)FindResource("AppCardBackground");

            foreach (UIElement child in BrowserContainer.Children)
            {
                child.Visibility = Visibility.Collapsed;
            }
            activeBrowser.Visibility = Visibility.Visible;
            AddressTextBox.Text = activeBrowser.Address;
        }

        private string ExtractVideoId(string url, string platform)
        {
            if (platform == "douyin")
            {
                var matchModal = Regex.Match(url, @"modal_id=(\d+)");
                if (matchModal.Success) return matchModal.Groups[1].Value;

                var matchVideo = Regex.Match(url, @"/video/(\d+)");
                if (matchVideo.Success) return matchVideo.Groups[1].Value;
            }
            else if (platform == "bilibili")
            {
                var matchBV = Regex.Match(url, @"(BV[a-zA-Z0-9]+)");
                if (matchBV.Success) return matchBV.Groups[1].Value;
            }
            else if (platform == "youtube")
            {
                var matchV = Regex.Match(url, @"(?:v=|youtu\.be/|/shorts/)([\w-]+)");
                if (matchV.Success) return matchV.Groups[1].Value;
            }
            else if (platform == "xiaohongshu")
            {
                var matchExplore = Regex.Match(url, @"explore/([\w-]+)");
                if (matchExplore.Success) return matchExplore.Groups[1].Value;
            }

            return null;
        }

        private async void FetchVideoDetailInBackground(string videoId, string platform,string address)
        {
            ResetVideoInfoUI("正在解析视频信息...", "正在解析视频信息"); // 增加Loading提示

            // 1. 获取 CefSharp 全局 Cookie
            var cookieManager = Cef.GetGlobalCookieManager();
            var visitor = new CefSharpHandlers.CookieVisitor();
            cookieManager.VisitAllCookies(visitor);
            var cookies = await visitor.Task;

            // 2. 将此 Cookie 转换为 Netscape 格式并写入临时文件
            var cookieDtos = cookies.Where(c => !string.IsNullOrEmpty(c.Domain) && c.Domain.Contains(platform))
                                    .Select(c => new CookieDto
                                    {
                                        Name = c.Name,
                                        Value = c.Value,
                                        Domain = c.Domain,
                                        Path = string.IsNullOrEmpty(c.Path) ? "/" : c.Path,
                                        Secure = c.Secure ?? false
                                    }).ToList();

            // 3. 使用 解析视频 (后台隐式进程)
            var parser = _videoParserFactory.GetParserByPlatform(platform);
            if (parser != null)
            {
                try
                {
                    var videoInfo = await parser.ExtractVideoDetailAsync(videoId, cookieDtos, address);
                    if (videoInfo != null)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            _currentVideoInfo = videoInfo;
                            TxtVideoTitle.Text = string.IsNullOrEmpty(_currentVideoInfo.Title) ? "获取失败" : _currentVideoInfo.Title;
                            TxtVideoAuthor.Text = "UP: " + (string.IsNullOrEmpty(_currentVideoInfo.Author) ? "获取失败" : _currentVideoInfo.Author);
                            TxtVideoTime.Text = "ID: " + _currentVideoInfo.VideoId;
                            TxtVideoStats.Text = "播放: " + _currentVideoInfo.PlayCount + " | 点赞: " + _currentVideoInfo.DiggCount + " | 收藏: " + _currentVideoInfo.CollectCount;
                            TxtVideoDesc.Text = string.IsNullOrEmpty(_currentVideoInfo.Description) ? "" : _currentVideoInfo.Description;
                            if (BtnAddToRepo != null) BtnAddToRepo.IsEnabled = true;
                        });
                        return; // 如果成功，直接返回，不再后续操作
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"解析异常: {ex.Message}", ex);
                    System.Diagnostics.Debug.WriteLine($"解析异常: {ex.Message}");
                }
            }
        }

        private void ResetVideoInfoUI(string message, string title = "暂无视频信息")
        {
            _currentVideoInfo = null;
            TxtVideoTitle.Text = title;
            TxtVideoAuthor.Text = "UP: -";
            TxtVideoTime.Text = "ID: -";
            TxtVideoStats.Text = "点赞: - | 播放: -";
            TxtVideoDesc.Text = message;
            if (BtnAddToRepo != null) BtnAddToRepo.IsEnabled = false;
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            foreach (UIElement child in BrowserContainer.Children)
            {
                if (child is ChromiumWebBrowser browser && browser.Visibility == Visibility.Visible)
                {
                    browser.Reload();
                    break;
                }
            }
        }

        public void BtnGo_Click(object sender, RoutedEventArgs e)
        {
            NavigateToAddress();
        }

        private void AddressTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                NavigateToAddress();
            }
        }

        private void NavigateToAddress()
        {
            foreach (UIElement child in BrowserContainer.Children)
            {
                if (child is ChromiumWebBrowser browser && browser.Visibility == Visibility.Visible)
                {
                    if (browser.Address == AddressTextBox.Text && !string.IsNullOrEmpty(browser.Address))
                    {
                        browser.Reload();
                    }
                    else
                    {
                        browser.Address = AddressTextBox.Text;
                    }
                    break;
                }
            }
        }
        
        private void NavigateInCurrentTab(string url)
        {
            foreach (UIElement child in BrowserContainer.Children)
            {
                if (child is ChromiumWebBrowser browser && browser.Visibility == Visibility.Visible)
                {
                    browser.Address = url;
                    break;
                }
            }
        }

        private async void BtnDownloadVideo_Click(object sender, RoutedEventArgs e)
        {
            if (_currentVideoInfo == null || _currentVideoInfo.VideoStreams == null || _currentVideoInfo.VideoStreams.Count == 0)
            {
                MessageBox.Show("没有可用的视频流进行下载。");
                return;
            }

            VideoStream streamToDownload = null;

            if (_currentVideoInfo.VideoStreams.Count == 1)
            {
                streamToDownload = _currentVideoInfo.VideoStreams[0];
            }
            else
            {
                var qualitySelector = new Window
                {
                    Title = "选择清晰度",
                    SizeToContent = SizeToContent.WidthAndHeight,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = this
                };
                var panel = new StackPanel { Margin = new Thickness(10) };
                qualitySelector.Content = panel;

                foreach (var stream in _currentVideoInfo.VideoStreams)
                {
                    var button = new Button { Content = stream.GearName, Margin = new Thickness(5) };
                    button.Click += (s, args) =>
                    {
                        streamToDownload = stream;
                        qualitySelector.DialogResult = true;
                        qualitySelector.Close();
                    };
                    panel.Children.Add(button);
                }

                qualitySelector.ShowDialog();
            }

            if (streamToDownload != null)
            {
                var downloadUrl = Regex.Replace(streamToDownload.Url, @"-web\.douyinvod\.com", "-cold.douyinvod.com");
                await DownloadFileAsync(downloadUrl, $"{_currentVideoInfo.VideoId}_{streamToDownload.GearName}.mp4");
            }
        }

        private async Task DownloadFileAsync(string url, string fileName)
        {
            var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
            string downloadDirectory = @"D:\SelfMediaDownloads";
            if (File.Exists(configPath))
            {
                try
                {
                    var configJson = File.ReadAllText(configPath);
                    var configObj = JsonNode.Parse(configJson);
                    if (configObj != null && configObj["DownloadPath"] != null)
                    {
                        downloadDirectory = configObj["DownloadPath"].ToString();
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Error("读取配置异常", ex);
                }
            }
            else
            {
                File.WriteAllText(configPath, "{\n  \"DownloadPath\": \"D:\\\\SelfMediaDownloads\"\n}");
            }

            Directory.CreateDirectory(downloadDirectory);
            var destinationPath = Path.Combine(downloadDirectory, fileName);

            var taskItem = new MockTaskItem
            {
                TaskInfo = fileName,
                Type = "视频下载",
                Progress = 0,
                Status = "下载中",
                UpdateTime = DateTime.Now.ToShortTimeString()
            };
            //_viewModel.AddTask(taskItem);

            using (var client = new HttpClient())
            {
                try
                {
                    using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode();

                        var totalBytes = response.Content.Headers.ContentLength;

                        using (var contentStream = await response.Content.ReadAsStreamAsync())
                        using (var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                        {
                            var totalBytesRead = 0L;
                            var buffer = new byte[8192];
                            var isMoreToRead = true;

                            do
                            {
                                var bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length);
                                if (bytesRead == 0)
                                {
                                    isMoreToRead = false;
                                }
                                else
                                {
                                    await fileStream.WriteAsync(buffer, 0, bytesRead);

                                    totalBytesRead += bytesRead;
                                    if (totalBytes.HasValue)
                                    {
                                        taskItem.Progress = (double)totalBytesRead / totalBytes.Value * 100;
                                    }
                                }
                            }
                            while (isMoreToRead);
                        }
                    }
                    taskItem.Status = "完成";
                    taskItem.Progress = 100;
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"下载失败: {ex.Message}", ex);
                    taskItem.Status = "失败";
                    MessageBox.Show($"下载失败: {ex.Message}");
                }
            }
        }

        private void MenuListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MenuLog != null && MenuLog.IsSelected)
            {
                var logWin = new Views.LogWindow();
                logWin.Owner = this;
                logWin.Show();
                ResetStatusLog();
                return;
            }
            if (HomePanel == null || BasicSettingsPanel == null || LLMSettingsPanel == null || AddKnowledgeBasePanel == null) return;
            
            HomePanel.Visibility = Visibility.Collapsed;
            BasicSettingsPanel.Visibility = Visibility.Collapsed;
            LLMSettingsPanel.Visibility = Visibility.Collapsed;
            AddKnowledgeBasePanel.Visibility = Visibility.Collapsed;
            AboutMePanel.Visibility = Visibility.Collapsed;
            //切换页面时暂停视频播放，避免资源占用和声音干扰
            PauseVideo();
            AddKnowledgeBasePanel.VideoPlayer?.MediaPlayer?.Pause();

            if (MenuBasicSettings != null && MenuBasicSettings.IsSelected)
            {
                BasicSettingsPanel.Visibility = Visibility.Visible;
            }
            else if (MenuLLMSettings != null && MenuLLMSettings.IsSelected)
            {
                LLMSettingsPanel.Visibility = Visibility.Visible;
            }
            else if (MenuAddKnowledgeBase != null && MenuAddKnowledgeBase.IsSelected)
            {
                AddKnowledgeBasePanel.Visibility = Visibility.Visible;
            }
            else if (MenuAbout != null && MenuAbout.IsSelected)
            {
                AboutMePanel.Visibility = Visibility.Visible;
            }
            else
            {
                HomePanel.Visibility = Visibility.Visible;
            }
        }

        private void MenuListBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!e.Handled)
            {
                e.Handled = true;
                var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
                {
                    RoutedEvent = UIElement.MouseWheelEvent,
                    Source = sender
                };
                var parent = ((Control)sender).Parent as UIElement;
                parent?.RaiseEvent(eventArg);
            }
        }

        private void BtnViewLogs_Click(object sender, RoutedEventArgs e)
        {
            var logWin = new Views.LogWindow();
            logWin.Owner = this;
            logWin.Show();
            
            ResetStatusLog();
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            //await CheckYtDlpStatusAsync();
        }

        
        private void DeleteRecord_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is ProcessingRecord record)
            {
                if (MessageBox.Show($"确定要删除记录 '{record.Title}' 吗？", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    _processingRecordRepository.Delete(record.VideoId);
                    _viewModel.LoadProcessingRecords();
                }
            }
        }
    }
}

