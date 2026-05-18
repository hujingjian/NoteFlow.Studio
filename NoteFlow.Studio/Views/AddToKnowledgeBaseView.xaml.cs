using LibVLCSharp.Shared;
using LibVLCSharp.WPF;
using Markdig;
using Microsoft.Win32;
using PaddleOCRSharp;
using NoteFlow.Studio.Model;
using NoteFlow.Studio.Model.Config;
using NoteFlow.Studio.Repository.Config;
using NoteFlow.Studio.Service.Models;
using NoteFlow.Studio.Service.Providers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Whisper.net;
using Xabe.FFmpeg;

namespace NoteFlow.Studio.Views
{
    public partial class AddToKnowledgeBaseView : UserControl
    {
        private bool _isTaskRunning = false;
        private string _pageUrl = string.Empty;
        private IEnumerable<CookieDto> _cookies = new List<CookieDto>();
        private string _onlineVideoUrl = string.Empty;
        private string _currentVideoId = string.Empty;
        private string _currentVideoUrl = string.Empty;
        private string _currentVideoPlatform = string.Empty;
        private bool _isDrawing = false;
        private System.Windows.Point _startPoint;
        private LibVLC _libVLC;
        private MediaPlayer _mediaPlayer;
        private readonly LLMService _llmService;
        private List<string> _snapshotImagePaths = new List<string>();
        private System.Threading.CancellationTokenSource _globalCts;

        public event EventHandler TaskCompleted;

        public AddToKnowledgeBaseView()
        {
            InitializeComponent();
            Core.Initialize();
            this.Loaded += AddToKnowledgeBaseView_Loaded;

            _libVLC = new LibVLC();
            _mediaPlayer = new MediaPlayer(_libVLC);
            VideoPlayer.MediaPlayer = _mediaPlayer; // 提前将 MediaPlayer 实例绑定到 VideoView

            _mediaPlayer.PositionChanged += MediaPlayer_PositionChanged;
            _mediaPlayer.TimeChanged += MediaPlayer_TimeChanged;
            _mediaPlayer.LengthChanged += MediaPlayer_LengthChanged;

            _llmService = new LLMService();
        }

        private async void AddToKnowledgeBaseView_Loaded(object sender, RoutedEventArgs e)
        {
            await CheckFfmpegStatusAsync();
            InitWhisperModels();
            LoadKnowledgeBases();
        }

        private void LoadKnowledgeBases()
        {
            CmbKnowledgeBase.Items.Clear();
            var configRepo = new ConfigRepository();
            var obsidianRoot = configRepo.GetConfig(AppConfigKeys.ObsidianRootPath, "");
            if (!string.IsNullOrEmpty(obsidianRoot) && Directory.Exists(obsidianRoot))
            {
                var dirs = Directory.GetDirectories(obsidianRoot);
                foreach (var dir in dirs)
                {
                    var dirName = Path.GetFileName(dir);
                    if (!dirName.StartsWith(".")) // Exclude hidden folders like .git
                    {
                        var item = new ComboBoxItem { Content = dirName, Tag = dir };
                        CmbKnowledgeBase.Items.Add(item);
                    }
                }
                if (CmbKnowledgeBase.Items.Count > 0)
                {
                    CmbKnowledgeBase.SelectedIndex = 0;
                }
            }
        }

        private string GetToolDownloadPath()
        {
            var configRepo = new ConfigRepository();
            return configRepo.GetConfig(AppConfigKeys.ToolDownloadPath, "D:\\Downloads\\Tools");
        }

        private string FormatSize(long bytes)
        {
            string[] suf = { "B", "KB", "MB", "GB", "TB" };
            if (bytes == 0) return "0 B";
            long bytesAbs = Math.Abs(bytes);
            int place = Convert.ToInt32(Math.Floor(Math.Log(bytesAbs, 1024)));
            if (place >= suf.Length) place = suf.Length - 1;
            double num = Math.Round(bytesAbs / Math.Pow(1024, place), 1);
            return (Math.Sign(bytes) * num).ToString() + " " + suf[place];
        }

        private string FormatDuration(long durationInSeconds)
        {
            TimeSpan ts = TimeSpan.FromMicroseconds(durationInSeconds);
            return ts.ToString(@"hh\:mm\:ss");
        }
        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            // 停止并清除之前的播放内容
            if (_mediaPlayer.IsPlaying)
            {
                _mediaPlayer.Stop();
            }
            //_mediaPlayer.Media = null;

            var window = Window.GetWindow(this) as MainWindow;
            if (window != null)
            {
                window.MenuHome.IsSelected = true;
            }
        }
        
        private void RbResultView_Checked(object sender, RoutedEventArgs e)
        {
            if (TxtOcrResult == null || TxtPureOcrResult == null) return;
            
            if (RbWithTime.IsChecked == true)
            {
                TxtOcrResult.Visibility = Visibility.Visible;
                TxtPureOcrResult.Visibility = Visibility.Collapsed;
            }
            else
            {
                TxtOcrResult.Visibility = Visibility.Collapsed;
                TxtPureOcrResult.Visibility = Visibility.Visible;
            }
        }

        private void BtnClearResult_Click(object sender, RoutedEventArgs e)
        {
            TxtOcrResult.Text = "";
            TxtPureOcrResult.Text = "";
        }

        private void BtnCopyResult_Click(object sender, RoutedEventArgs e)
        {
            if (TxtOcrResult.Visibility == Visibility.Visible)
            {
                if (!string.IsNullOrEmpty(TxtOcrResult.Text))
                {
                    Clipboard.SetText(TxtOcrResult.Text);
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(TxtPureOcrResult.Text))
                {
                    Clipboard.SetText(TxtPureOcrResult.Text);
                }
            }
        }

        private void TxtResult_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (BtnGenerateMarkdown != null && TxtOcrResult != null && TxtPureOcrResult != null)
            {
                BtnGenerateMarkdown.IsEnabled = !string.IsNullOrWhiteSpace(TxtOcrResult.Text) || !string.IsNullOrWhiteSpace(TxtPureOcrResult.Text);
            }
        }
    }
}

