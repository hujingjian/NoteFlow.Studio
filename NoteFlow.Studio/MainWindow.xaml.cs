using NoteFlow.Studio.Infrastructure.Logging;
using NoteFlow.Studio.Repository.Config;
using NoteFlow.Studio.Service.Models;
using NoteFlow.Studio.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NoteFlow.Studio
{
    public partial class MainWindow : Window
    {
        private enum ShellPage
        {
            Home,
            BasicSettings,
            LLMSettings,
            AddKnowledgeBase,
            FeishuCapture,
            VideoGeneration,
            VideoRecreation,
            RemotionHistory,
            About
        }

        private readonly MainViewModel _viewModel;
        private readonly Dictionary<ShellPage, FrameworkElement> _pages;

        private readonly BrowserHomeView _homePanel;
        private readonly BasicSettingsView _basicSettingsPanel;
        private readonly LLMSettingsView _llmSettingsPanel;
        private readonly AddToKnowledgeBaseView _addKnowledgeBasePanel;
        private readonly FeishuArticleCaptureView _feishuCapturePanel;
        private readonly VideoGenerationView _videoGenerationPanel;
        private readonly VideoRecreationView _videoRecreationPanel;
        private readonly RemotionHistoryView _remotionHistoryPanel;
        private readonly AboutMeView _aboutMePanel;

        private int _errorCount = 0;
        private int _warningCount = 0;

        public MainWindow()
        {
            AppLogger.OnLogReceived += log =>
            {
                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    AppLogger.AddMemoryLogFromUI(log);
                    UpdateStatusLog(log);
                });
            };

            InitializeComponent();

            _viewModel = new MainViewModel();
            DataContext = _viewModel;
            _viewModel.LoadProcessingRecords();

            _homePanel = new BrowserHomeView();
            _basicSettingsPanel = new BasicSettingsView();
            _llmSettingsPanel = new LLMSettingsView();
            _addKnowledgeBasePanel = new AddToKnowledgeBaseView();
            _feishuCapturePanel = new FeishuArticleCaptureView();
            _videoGenerationPanel = new VideoGenerationView();
            _videoRecreationPanel = new VideoRecreationView();
            _remotionHistoryPanel = new RemotionHistoryView();
            _aboutMePanel = new AboutMeView();

            _pages = new Dictionary<ShellPage, FrameworkElement>
            {
                [ShellPage.Home] = _homePanel,
                [ShellPage.BasicSettings] = _basicSettingsPanel,
                [ShellPage.LLMSettings] = _llmSettingsPanel,
                [ShellPage.AddKnowledgeBase] = _addKnowledgeBasePanel,
                [ShellPage.FeishuCapture] = _feishuCapturePanel,
                [ShellPage.VideoGeneration] = _videoGenerationPanel,
                [ShellPage.VideoRecreation] = _videoRecreationPanel,
                [ShellPage.RemotionHistory] = _remotionHistoryPanel,
                [ShellPage.About] = _aboutMePanel
            };

            _homePanel.RefreshProcessingRecordsRequested += (s, e) => _viewModel.LoadProcessingRecords();
            _homePanel.AddToKnowledgeBaseRequested += HomePanel_AddToKnowledgeBaseRequested;

            _addKnowledgeBasePanel.TaskCompleted += (s, e) => _viewModel.LoadProcessingRecords();
            _remotionHistoryPanel.OnViewRecordRequested += id =>
            {
                _videoGenerationPanel.LoadRecord(id);
                MenuVideoGeneration.IsSelected = true;
            };

            LoadGlobalAiModels();
            ShowPage(ShellPage.Home);
            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;
        }

        private void HomePanel_AddToKnowledgeBaseRequested(object sender, VideoInfo videoInfo)
        {
            if (MenuAddKnowledgeBase != null)
            {
                MenuAddKnowledgeBase.IsSelected = true;
            }

            _addKnowledgeBasePanel.ResetVideoInfo();
            _addKnowledgeBasePanel.SetVideoInfo(videoInfo);
        }

        private void UpdateStatusLog(LogEntry log)
        {
            if (log.Level == "ERROR") _errorCount++;
            else if (log.Level == "WARN") _warningCount++;

            if (_errorCount > 0)
            {
                IconStatusLight.Foreground = Brushes.Red;
                TxtStatusLog.Text = $"存在错误: {_errorCount} 个";
            }
            else if (_warningCount > 0)
            {
                IconStatusLight.Foreground = Brushes.Yellow;
                TxtStatusLog.Text = $"存在警告: {_warningCount} 个";
            }
            else
            {
                IconStatusLight.Foreground = Brushes.LimeGreen;
                TxtStatusLog.Text = "系统运行正常";
            }
        }

        public void ResetStatusLog()
        {
            _errorCount = 0;
            _warningCount = 0;
            if (IconStatusLight != null) IconStatusLight.Foreground = Brushes.LimeGreen;
            if (TxtStatusLog != null) TxtStatusLog.Text = "系统运行正常";
        }

        public void RefreshProcessingRecords()
        {
            _viewModel.LoadProcessingRecords();
        }

        public void LoadGlobalAiModels()
        {
            var repo = new AiModelRepository();
            var activeModels = repo.GetAll().Where(m => m.IsEnabled).ToList();
            if (CmbGlobalModel != null)
            {
                CmbGlobalModel.ItemsSource = activeModels;
                if (activeModels.Count > 0) CmbGlobalModel.SelectedIndex = 0;
            }
        }

        public string GetCurrentBrowserUrl()
        {
            return _homePanel.GetCurrentBrowserUrl();
        }

        public void NavigateHomeToUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;

            MenuHome.IsSelected = true;
            ShowPage(ShellPage.Home);
            _homePanel.NavigateToUrl(url);
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void BtnMaximize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void MenuListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MenuLog?.IsSelected == true)
            {
                ShowLogWindow();
                return;
            }

            ShowPage(GetSelectedPage());
        }

        private ShellPage GetSelectedPage()
        {
            if (MenuBasicSettings?.IsSelected == true) return ShellPage.BasicSettings;
            if (MenuLLMSettings?.IsSelected == true) return ShellPage.LLMSettings;
            if (MenuAddKnowledgeBase?.IsSelected == true) return ShellPage.AddKnowledgeBase;
            if (MenuFeishuCapture?.IsSelected == true) return ShellPage.FeishuCapture;
            if (MenuVideoGeneration?.IsSelected == true) return ShellPage.VideoGeneration;
            if (MenuVideoRecreation?.IsSelected == true) return ShellPage.VideoRecreation;
            if (MenuRemotionHistory?.IsSelected == true) return ShellPage.RemotionHistory;
            if (MenuAbout?.IsSelected == true) return ShellPage.About;

            return ShellPage.Home;
        }

        private void ShowPage(ShellPage page)
        {
            if (_pages == null || PageHost == null)
            {
                return;
            }

            PauseActiveMedia();

            if (page == ShellPage.RemotionHistory)
            {
                _remotionHistoryPanel.LoadData();
            }
            else if (page == ShellPage.VideoRecreation)
            {
                _videoRecreationPanel.RefreshModels();
            }

            PageHost.Content = _pages[page];
        }

        private void PauseActiveMedia()
        {
            _homePanel.PauseVideo();
            _addKnowledgeBasePanel.PauseVideoPreview();
            _videoRecreationPanel.PauseVideo();
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            _addKnowledgeBasePanel.DisposeVideoResources();
            _feishuCapturePanel.DisposeBrowser();
            _videoRecreationPanel.Dispose();
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
            ShowLogWindow();
        }

        private void ShowLogWindow()
        {
            var logWin = new LogWindow { Owner = this };
            logWin.Show();
            ResetStatusLog();
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // await CheckYtDlpStatusAsync();
        }
    }
}
