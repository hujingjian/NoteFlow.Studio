using CefSharp;
using CefSharp.Handler;
using CefSharp.Wpf;
using Microsoft.Win32;
using NoteFlow.Studio.Infrastructure.Logging;
using NoteFlow.Studio.Model;
using NoteFlow.Studio.Model.Config;
using NoteFlow.Studio.Repository;
using NoteFlow.Studio.Service.Models;
using NoteFlow.Studio.Services;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace NoteFlow.Studio.Views
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


    public partial class BrowserHomeView : UserControl
    {
        public event EventHandler<VideoInfo> AddToKnowledgeBaseRequested;
        public event EventHandler RefreshProcessingRecordsRequested;

        private readonly ProcessingRecordRepository _processingRecordRepository = new ProcessingRecordRepository();
        private readonly BrowserVideoMetadataService _videoMetadataService = new BrowserVideoMetadataService();
        private int _tabCounter = 1;
        private string _lastFetchedVideoId;
        private VideoInfo _currentVideoInfo;
        

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
            RefreshProcessingRecordsRequested?.Invoke(this, EventArgs.Empty);

            AddToKnowledgeBaseRequested?.Invoke(this, _currentVideoInfo);

            PauseVideo();

        }

        private void BtnSelectLocalVideo_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择本地视频",
                Filter = "视频文件|*.mp4;*.mov;*.mkv;*.avi;*.wmv;*.flv;*.webm;*.m4v|所有文件|*.*",
                Multiselect = false
            };

            if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            {
                return;
            }

            var filePath = dialog.FileName;
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                MessageBox.Show("未找到选择的视频文件。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var fileInfo = new FileInfo(filePath);
            var title = Path.GetFileNameWithoutExtension(filePath);
            var videoInfo = new VideoInfo
            {
                Platform = "本地视频",
                PageUrl = filePath,
                VideoId = $"local_{DateTime.Now:yyyyMMddHHmmssfff}",
                Title = string.IsNullOrWhiteSpace(title) ? fileInfo.Name : title,
                Description = "本地选择视频",
                VideoUrl = filePath,
                Size = fileInfo.Length
            };

            var record = new ProcessingRecord
            {
                VideoId = videoInfo.VideoId,
                Title = videoInfo.Title,
                Description = videoInfo.Description,
                VideoUrl = videoInfo.PageUrl,
                CoverUrl = string.Empty,
                ProcessingStartTime = DateTime.Now,
                Status = "等待中"
            };

            _processingRecordRepository.AddOrUpdate(record);
            RefreshProcessingRecordsRequested?.Invoke(this, EventArgs.Empty);
            AddToKnowledgeBaseRequested?.Invoke(this, videoInfo);
        }

        public void PauseVideo()
        {
            // 暂停当前页面的视频播放
            foreach (UIElement child in BrowserContainer.Children)
            {
                if (child is ChromiumWebBrowser currentBrowser && currentBrowser.Visibility == Visibility.Visible)
                {
                    try
                    {
                        if (!currentBrowser.IsBrowserInitialized || currentBrowser.IsDisposed)
                        {
                            return;
                        }

                        currentBrowser.GetMainFrame()?.ExecuteJavaScriptAsync("document.querySelectorAll('video').forEach(v => v.pause());");
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warn($"暂停浏览器视频失败: {ex.Message}");
                    }
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

                    if (!string.IsNullOrEmpty(address) &&
                        _videoMetadataService.TryCreateRequest(address, out var request))
                    {
                        if (!string.IsNullOrEmpty(request.VideoId))
                        {
                            //if (videoId != _lastFetchedVideoId)
                            {
                                _lastFetchedVideoId = request.VideoId;
                                FetchVideoDetailInBackground(request);
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
                    try
                    {
                        if (browser.IsBrowserInitialized && !browser.IsDisposed)
                        {
                            browser.Address = "about:blank";
                            browser.GetBrowser()?.StopLoad();
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warn($"关闭浏览器标签页时停止加载失败: {ex.Message}");
                    }
                    
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

            // 关键：初始化后立即开启 IME
            browser.Loaded += (s, e) =>
            {
                browser.WpfKeyboardHandler = new CefSharp.Wpf.Experimental.WpfImeKeyboardHandler(browser);
            };

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

        private async void FetchVideoDetailInBackground(BrowserVideoRequest request)
        {
            ResetVideoInfoUI("正在解析视频信息...", "正在解析视频信息"); // 增加Loading提示

            try
            {
                var videoInfo = await _videoMetadataService.FetchVideoDetailAsync(request);
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
                    return;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error($"解析异常: {ex.Message}", ex);
                System.Diagnostics.Debug.WriteLine($"解析异常: {ex.Message}");
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

        public void NavigateToUrl(string url)
        {
            AddressTextBox.Text = url;
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
                    Owner = Window.GetWindow(this)
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



        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool MoveWindow(IntPtr hwnd, int x, int y, int cx, int cy, bool repaint);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SetFocus(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SetActiveWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);



        private const int GWL_STYLE = -16;
        private const int GWL_EXSTYLE = -20;
        private const int WS_VISIBLE = 0x10000000;
        private const int WS_CHILD = 0x40000000;
        private const int WS_CAPTION = 0x00C00000;
        private const int WS_THICKFRAME = 0x00040000;
        private const int WS_BORDER = 0x00800000;
        private const int WS_EX_CLIENTEDGE = 0x00000200;
        private const int WS_EX_CONTROLPARENT = 0x00010000;
        private const int SW_SHOW = 5;

        private System.Diagnostics.Process _cliProcess;

        private void ForceRefocusConsole(IntPtr childHandle)
        {
            if (childHandle == IntPtr.Zero) return;
            uint wpfThreadId = GetCurrentThreadId();
            uint childThreadId = GetWindowThreadProcessId(childHandle, out _);

            if (wpfThreadId != childThreadId)
            {
                AttachThreadInput(wpfThreadId, childThreadId, true);
                FocusTerminal(childHandle);
                SetActiveWindow(childHandle);
                AttachThreadInput(wpfThreadId, childThreadId, false);
            }
            else
            {
                FocusTerminal(childHandle);
                SetActiveWindow(childHandle);
            }
        }
        void FocusTerminal(IntPtr childHandle)
        {
            ShowWindow(childHandle, SW_SHOW);
            SetForegroundWindow(childHandle);
            SetFocus(childHandle);
        }

        private async void BtnStartCli_Click(object sender, RoutedEventArgs e)
        {
            if (_cliProcess != null && !_cliProcess.HasExited)
            {
                if (MessageBox.Show("CLI 已经在运行中。是否要关闭当前终端并启动新的 Claude CLI？", "提示", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    try
                    {
                        _cliProcess.Kill();
                        _cliProcess.Dispose();
                    }
                    catch (Exception ex) 
                    { 
                        AppLogger.Error("关闭CLI进程异常", ex);
                    }
                    _cliProcess = null;
                }
                else
                {
                    return;
                }
            }

            var repo = new NoteFlow.Studio.Repository.Config.ConfigRepository();
            string startDir = repo.GetConfig(AppConfigKeys.ObsidianRootPath, "D:\\");

            _cliProcess = new System.Diagnostics.Process();
            _cliProcess.StartInfo.FileName = "cmd.exe";
            _cliProcess.StartInfo.Arguments = "/k claude"; 
            _cliProcess.StartInfo.WorkingDirectory = System.IO.Directory.Exists(startDir) ? startDir : "C:\\";
            _cliProcess.StartInfo.UseShellExecute = true; 
            _cliProcess.StartInfo.CreateNoWindow = false;
            _cliProcess.StartInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Minimized;

            try
            {
                _cliProcess.Start();
                
                while (_cliProcess.MainWindowHandle == IntPtr.Zero && !_cliProcess.HasExited)
                {
                    await Task.Delay(100);
                    _cliProcess.Refresh();
                }

                if (_cliProcess.HasExited) return;

                IntPtr childHandle = _cliProcess.MainWindowHandle;
                if (childHandle != IntPtr.Zero)
                {
                    var winFormsPanel = new System.Windows.Forms.Panel();
                    winFormsPanel.BackColor = System.Drawing.Color.Black;
                    winFormsPanel.BorderStyle = System.Windows.Forms.BorderStyle.None;
                    winFormsPanel.Margin = new System.Windows.Forms.Padding(0);
                    
                    TerminalHost.Child = winFormsPanel;
                    TerminalHost.Background = new SolidColorBrush(Colors.Black);
                    TerminalHost.Focusable = true;

                    // 使用底层跨线程输入附加，强行要求焦点
                    TerminalHost.PreviewMouseLeftButtonDown += (s, ev) => 
                    { 
                        //TerminalHost.Focus(); 
                        ForceRefocusConsole(childHandle);
                        ev.Handled = true;
                    };
                    //TerminalHost.GotKeyboardFocus += (s, ev) => ForceRefocusConsole(childHandle);

                    TerminalHost.GotFocus += (s, ev) => FocusTerminal(childHandle);

                    // 当主窗口被切走再回来(ALT+TAB) 或点击其他部分再回来时，也尽可能捕捉恢复光标
                    //this.Activated += (s, ev) => ForceRefocusConsole(childHandle);

                    SetParent(childHandle, winFormsPanel.Handle);

                    // 剥离原有的边框、标题栏等样式
                    int style = GetWindowLong(childHandle, GWL_STYLE);
                    style &= ~WS_CAPTION;
                    style &= ~WS_BORDER;
                    style &= ~WS_THICKFRAME;
                    style |= WS_VISIBLE | WS_CHILD;
                    SetWindowLong(childHandle, GWL_STYLE, style);

                    int exStyle = GetWindowLong(childHandle, GWL_EXSTYLE);
                    exStyle &= ~WS_EX_CLIENTEDGE;
                    exStyle |= WS_EX_CONTROLPARENT; // 允许父窗口导航到子窗口焦点
                    SetWindowLong(childHandle, GWL_EXSTYLE, exStyle);

                    winFormsPanel.Resize += (s, args) =>
                    {
                        MoveWindow(childHandle, 0, 0, winFormsPanel.Width, winFormsPanel.Height, true);
                    };
                    winFormsPanel.MouseClick += (s, ev) => FocusTerminal(childHandle);

                    //winFormsPanel.GotFocus += (s, ev) => FocusTerminal(childHandle);

                    MoveWindow(childHandle, 0, 0, winFormsPanel.Width, winFormsPanel.Height, true);

                    var source = (HwndSource)PresentationSource.FromVisual(TerminalHost);
                    if (source != null)
                    {
                        // 添加底层的 Win32 消息钩子
                        source.AddHook(ChildWindowMessageHook);
                    }

                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(200);
                        Dispatcher.Invoke(() => ForceRefocusConsole(childHandle));
                    });
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error($"启动CLI失败: {ex.Message}", ex);
                MessageBox.Show("启动CLI失败: " + ex.Message);
            }
        }
        // 定义钩子处理函数
        private IntPtr ChildWindowMessageHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // 0x0210 是 WM_PARENTNOTIFY，当子窗口发生鼠标点击时会发给父窗口
            const int WM_PARENTNOTIFY = 0x0210;
            const int WM_LBUTTONDOWN = 0x0201;

            if (msg == WM_PARENTNOTIFY)
            {
                // 检查是否是鼠标左键按下
                if ((wParam.ToInt32() & 0xFFFF) == WM_LBUTTONDOWN)
                {
                    if (_cliProcess != null && !_cliProcess.HasExited)
                    {
                        // 这里调用你的聚焦逻辑
                        ForceRefocusConsole(_cliProcess.MainWindowHandle);
                    }
                }
            }
            return IntPtr.Zero;
        }

        public BrowserHomeView()
        {
            InitializeComponent();

            AddNewBrowserTab("首页", "https://www.douyin.com");

            BtnDouyin.Click += (s, e) => NavigateInCurrentTab("https://www.douyin.com");
            BtnBilibili.Click += (s, e) => NavigateInCurrentTab("https://www.bilibili.com");
            BtnYoutube.Click += (s, e) => NavigateInCurrentTab("https://www.youtube.com");
        }

        private void DeleteRecord_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is ProcessingRecord record)
            {
                if (MessageBox.Show($"确定要删除记录 '{record.Title}' 吗？", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    _processingRecordRepository.Delete(record.VideoId);
                    RefreshProcessingRecordsRequested?.Invoke(this, EventArgs.Empty);
                }
            }
        }
    }
}
