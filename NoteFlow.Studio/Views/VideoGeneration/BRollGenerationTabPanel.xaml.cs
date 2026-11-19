using CefSharp;
using CefSharp.Wpf.Experimental;
using NoteFlow.Studio.Repository;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NoteFlow.Studio.Views.VideoGeneration
{
    public class BRollSceneItem : INotifyPropertyChanged
    {
        private string _selectedKeyword = string.Empty;

        [JsonPropertyName("sceneId")]
        public int SceneId { get; set; }

        [JsonPropertyName("start")]
        public string Start { get; set; } = string.Empty;

        [JsonPropertyName("end")]
        public string End { get; set; } = string.Empty;

        [JsonPropertyName("subtitle")]
        public string Subtitle { get; set; } = string.Empty;

        [JsonPropertyName("asset_type")]
        public string AssetType { get; set; } = string.Empty;

        [JsonPropertyName("reason")]
        public string Reason { get; set; } = string.Empty;

        [JsonPropertyName("search_keyword")]
        public List<string> SearchKeywords { get; set; } = new List<string>();

        [JsonPropertyName("website_url")]
        public string WebsiteUrl { get; set; } = string.Empty;

        [JsonPropertyName("html_animation_prompt")]
        public string HtmlAnimationPrompt { get; set; } = string.Empty;

        [JsonPropertyName("ai_image_prompt")]
        public string AiImagePrompt { get; set; } = string.Empty;

        [JsonPropertyName("ai_video_prompt")]
        public string AiVideoPrompt { get; set; } = string.Empty;

        public string TimeRange => string.IsNullOrWhiteSpace(Start) && string.IsNullOrWhiteSpace(End)
            ? string.Empty
            : $"{Start} - {End}".Trim(' ', '-');

        public string SelectedKeyword
        {
            get => _selectedKeyword;
            set
            {
                if (_selectedKeyword == value)
                {
                    return;
                }

                _selectedKeyword = value ?? string.Empty;
                OnPropertyChanged();
            }
        }

        public string SelectedPrompt
        {
            get
            {
                var assetType = AssetType?.Trim().ToLowerInvariant() ?? string.Empty;
                if (assetType.Contains("html") || assetType.Contains("animation"))
                {
                    return HtmlAnimationPrompt ?? string.Empty;
                }

                if (assetType.Contains("image") || assetType.Contains("screenshot"))
                {
                    return AiImagePrompt ?? string.Empty;
                }

                if (assetType.Contains("video"))
                {
                    return AiVideoPrompt ?? string.Empty;
                }

                return string.Empty;
            }
        }

        public string RelationText => string.IsNullOrWhiteSpace(WebsiteUrl) ? string.Empty : "网站";

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class MockAssetResult
    {
        public string Title { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string Duration { get; set; } = string.Empty;
        public string Resolution { get; set; } = string.Empty;
        public Brush PreviewBrush { get; set; } = Brushes.Transparent;
    }

    public partial class BRollGenerationTabPanel : UserControl
    {
        private readonly RemotionRepository _remotionRepo = new RemotionRepository();
        private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        private bool _isBrowserZoomed;

        public ObservableCollection<BRollSceneItem> Scenes { get; } = new ObservableCollection<BRollSceneItem>();
        public ObservableCollection<MockAssetResult> MockAssets { get; } = new ObservableCollection<MockAssetResult>();

        public string CurrentOverviewId { get; set; } = string.Empty;

        public BRollGenerationTabPanel()
        {
            InitializeComponent();
            LstBRollScenes.ItemsSource = Scenes;
            DataContext = this;

            BRollBrowser.Loaded += (s, e) =>
            {
                BRollBrowser.WpfKeyboardHandler = new WpfImeKeyboardHandler(BRollBrowser);
            };

            BRollBrowser.AddressChanged += (s, e) =>
            {
                Dispatcher.Invoke(() => TxtBrowserUrl.Text = BRollBrowser.Address ?? string.Empty);
            };

            BuildMockAssets("technology ai");
        }

        public void ResetForNewRemotion(string overviewId)
        {
            CurrentOverviewId = overviewId;
            LoadStoryboards();
        }

        public void LoadStoryboards()
        {
            Scenes.Clear();

            if (string.IsNullOrWhiteSpace(CurrentOverviewId))
            {
                UpdateSummary();
                return;
            }

            var storyboards = _remotionRepo.GetStoryboardsByOverviewId(CurrentOverviewId);
            foreach (var storyboard in storyboards)
            {
                var parsedItems = ParseBRollItems(storyboard.RawJson).ToList();
                if (parsedItems.Count == 0)
                {
                    parsedItems.Add(new BRollSceneItem
                    {
                        SceneId = storyboard.SceneId
                    });
                }

                foreach (var item in parsedItems)
                {
                    if (item.SceneId <= 0)
                    {
                        item.SceneId = storyboard.SceneId;
                    }

                    item.SelectedKeyword = item.SearchKeywords.FirstOrDefault() ?? string.Empty;
                    Scenes.Add(item);
                }
            }

            if (Scenes.Count > 0)
            {
                LstBRollScenes.SelectedIndex = 0;
            }

            UpdateSummary();
        }

        private IEnumerable<BRollSceneItem> ParseBRollItems(string rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return Enumerable.Empty<BRollSceneItem>();
            }

            try
            {
                using var document = JsonDocument.Parse(rawJson);
                if (document.RootElement.ValueKind == JsonValueKind.Array)
                {
                    return JsonSerializer.Deserialize<List<BRollSceneItem>>(rawJson, _jsonOptions) ?? Enumerable.Empty<BRollSceneItem>();
                }

                if (document.RootElement.ValueKind == JsonValueKind.Object)
                {
                    var item = JsonSerializer.Deserialize<BRollSceneItem>(rawJson, _jsonOptions);
                    return item == null ? Enumerable.Empty<BRollSceneItem>() : new[] { item };
                }
            }
            catch
            {
                return Enumerable.Empty<BRollSceneItem>();
            }

            return Enumerable.Empty<BRollSceneItem>();
        }

        private void UpdateSummary()
        {
            TxtSceneSummary.Text = $"共 {Scenes.Count} 个 B-Roll 分镜";
        }

        private void LstBRollScenes_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstBRollScenes.SelectedItem is not BRollSceneItem scene)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(scene.SelectedKeyword))
            {
                TxtAssetSearchKeyword.Text = scene.SelectedKeyword;
                BuildMockAssets(scene.SelectedKeyword);
            }

            if (!string.IsNullOrWhiteSpace(scene.WebsiteUrl))
            {
                NavigateBrowser(scene.WebsiteUrl);
            }
        }

        private void KeywordComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not ComboBox comboBox || comboBox.SelectedItem is not string keyword || string.IsNullOrWhiteSpace(keyword))
            {
                return;
            }

            if (comboBox.DataContext is BRollSceneItem scene)
            {
                scene.SelectedKeyword = keyword;
                LstBRollScenes.SelectedItem = scene;
            }

            TxtAssetSearchKeyword.Text = keyword;
            BuildMockAssets(keyword);
        }

        private void CopyPrompt_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { DataContext: BRollSceneItem scene } && !string.IsNullOrWhiteSpace(scene.SelectedPrompt))
            {
                Clipboard.SetText(scene.SelectedPrompt);
            }
        }

        private void Bookmark_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string url)
            {
                NavigateBrowser(url);
            }
        }

        private void BtnBrowserBack_Click(object sender, RoutedEventArgs e)
        {
            if (BRollBrowser.CanGoBack)
            {
                BRollBrowser.Back();
            }
        }

        private void BtnBrowserForward_Click(object sender, RoutedEventArgs e)
        {
            if (BRollBrowser.CanGoForward)
            {
                BRollBrowser.Forward();
            }
        }

        private void BtnBrowserRefresh_Click(object sender, RoutedEventArgs e)
        {
            BRollBrowser.Reload();
        }

        private void TxtBrowserUrl_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                NavigateBrowser(TxtBrowserUrl.Text);
            }
        }

        private void BtnBrowserGo_Click(object sender, RoutedEventArgs e)
        {
            NavigateBrowser(TxtBrowserUrl.Text);
        }

        private void BtnToggleBrowserZoom_Click(object sender, RoutedEventArgs e)
        {
            _isBrowserZoomed = !_isBrowserZoomed;

            if (_isBrowserZoomed)
            {
                StoryboardColumn.Width = new GridLength(220, GridUnitType.Pixel);
                BrowserColumn.Width = new GridLength(1, GridUnitType.Star);
                BrowserAreaRow.Height = new GridLength(2.6, GridUnitType.Star);
                AssetPreviewRow.Height = new GridLength(0.7, GridUnitType.Star);
                StoryboardPanel.Margin = new Thickness(0, 0, 8, 0);
                IconBrowserZoom.Kind = MaterialDesignThemes.Wpf.PackIconKind.ArrowCollapse;
                BtnToggleBrowserZoom.ToolTip = "还原浏览器";
                return;
            }

            StoryboardColumn.Width = new GridLength(1.90, GridUnitType.Star);
            BrowserColumn.Width = new GridLength(1.10, GridUnitType.Star);
            BrowserAreaRow.Height = new GridLength(1.35, GridUnitType.Star);
            AssetPreviewRow.Height = new GridLength(1, GridUnitType.Star);
            StoryboardPanel.Margin = new Thickness(0, 0, 12, 0);
            IconBrowserZoom.Kind = MaterialDesignThemes.Wpf.PackIconKind.ArrowExpand;
            BtnToggleBrowserZoom.ToolTip = "放大/还原浏览器";
        }

        private void BtnRefreshStoryboards_Click(object sender, RoutedEventArgs e)
        {
            LoadStoryboards();
        }

        private void BtnMockSearch_Click(object sender, RoutedEventArgs e)
        {
            BuildMockAssets(TxtAssetSearchKeyword.Text);
        }

        private void NavigateBrowser(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            var normalizedUrl = url.Trim();
            if (!normalizedUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !normalizedUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                normalizedUrl = "https://" + normalizedUrl;
            }

            TxtBrowserUrl.Text = normalizedUrl;
            BRollBrowser.Address = normalizedUrl;
        }

        private void BuildMockAssets(string keyword)
        {
            MockAssets.Clear();

            var normalizedKeyword = string.IsNullOrWhiteSpace(keyword) ? "b-roll" : keyword.Trim();
            var brushes = new[]
            {
                CreateBrush("#0EA5E9", "#1E293B"),
                CreateBrush("#22C55E", "#0F172A"),
                CreateBrush("#A855F7", "#111827"),
                CreateBrush("#F97316", "#1F2937"),
                CreateBrush("#14B8A6", "#111827"),
                CreateBrush("#E11D48", "#1E1B4B")
            };

            for (var i = 0; i < 6; i++)
            {
                MockAssets.Add(new MockAssetResult
                {
                    Title = $"{normalizedKeyword} 素材 {i + 1}",
                    Source = i % 3 == 0 ? "Pexels Mock" : i % 3 == 1 ? "Pixabay Mock" : "Mixkit Mock",
                    Duration = i % 2 == 0 ? $"0:{15 + i * 3:00}" : "图片",
                    Resolution = i % 2 == 0 ? "1920x1080" : "3840x2160",
                    PreviewBrush = brushes[i % brushes.Length]
                });
            }
        }

        private static Brush CreateBrush(string start, string end)
        {
            return new LinearGradientBrush(
                (Color)ColorConverter.ConvertFromString(start),
                (Color)ColorConverter.ConvertFromString(end),
                new Point(0, 0),
                new Point(1, 1));
        }
    }
}
