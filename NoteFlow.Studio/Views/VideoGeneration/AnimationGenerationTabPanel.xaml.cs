using CefSharp;
using CefSharp.DevTools.DOM;
using CefSharp.DevTools.Page;
using CefSharp.Wpf;
using NoteFlow.Studio.Infrastructure.Logging;
using NoteFlow.Studio.Model;
using NoteFlow.Studio.Model.Config;
using NoteFlow.Studio.Repository;
using NoteFlow.Studio.Repository.Config;
using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Net.Http;
using System.IO;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace NoteFlow.Studio.Views.VideoGeneration
{
    public class StoryboardItem : System.ComponentModel.INotifyPropertyChanged
    {
        public int Index { get; set; }
        public string Title { get; set; }
        public string TimeRange { get; set; }

        public string RawJson { get; set; }

        private bool _isCompleted;
        public bool IsCompleted
        {
            get => _isCompleted;
            set { _isCompleted = value; OnPropertyChanged(nameof(IsCompleted)); }
        }

        private bool _isReference;
        public bool IsReference
        {
            get => _isReference;
            set { _isReference = value; OnPropertyChanged(nameof(IsReference)); }
        }

        public string ReferenceHtml { get; set; }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
    }

    public class StyleOption
    {
        public string Name { get; set; }
        public string Description { get; set; }
    }

    public partial class AnimationGenerationTabPanel : UserControl
    {
        private PromptRepository _promptRepo;
        private bool _isEditingPrompt = false;
        private bool _isAddingPrompt = false;
        private bool _isUpdatingAnimationCheckBoxes = false;
        private Prompt _currentPrompt;

        public event Action<string> ExecuteScriptRequested;

        public string CurrentOverviewId { get; set; }
        private readonly RemotionRepository _remotionRepo = new RemotionRepository();
        // 记录当前选中的分镜ID
        private int _currentSelectedSceneId = 1;

        public string ScriptResultText
        {
            get { return TxtScriptRaw.Text; }
            set
            {
                TxtScriptRaw.Text = value;
                ParseScriptJson(value);
            }
        }

        public AnimationGenerationTabPanel()
        {
            InitializeComponent();
            _promptRepo = new PromptRepository();
            Loaded += AnimationGenerationTabPanel_Loaded;
        }

        private void AnimationGenerationTabPanel_Loaded(object sender, RoutedEventArgs e)
        {
            LoadPrompts();
            LoadStyles();
        }

        public void ResetForNewRemotion(string overviewId)
        {
            CurrentOverviewId = overviewId;
            _currentSelectedSceneId = 1;
            TxtScriptRaw.Text = string.Empty;
            LstStoryboards.Items.Clear();
            TxtHtmlContent.Text = string.Empty;
            if (TabHtmlEdit != null) TabHtmlEdit.IsChecked = true;
            if (TxtHtmlContent != null) TxtHtmlContent.Visibility = Visibility.Visible;
            if (PreviewViewbox != null) PreviewViewbox.Visibility = Visibility.Collapsed;
            if (CmbStyles != null && CmbStyles.Items.Count > 0) CmbStyles.SelectedIndex = 0;
            if (CmbResolution != null) CmbResolution.SelectedIndex = 0;
            if (CmbFps != null) CmbFps.SelectedIndex = 1;
            if (ChkTransparentBg != null) ChkTransparentBg.IsChecked = false;
            if (ChkAllowCenterArea != null) ChkAllowCenterArea.IsChecked = false;
            ToggleEditMode(false);
            if (_isPreviewZoomed)
            {
                BtnToggleZoom_Click(this, new RoutedEventArgs());
            }
        }

        private void LoadStyles()
        {
            var styles = new List<StyleOption>
            {
                new StyleOption { Name = "", Description = "" },
                new StyleOption { Name = "科技科普-深色", Description = " \r\n 类型：科技科普 / AI原理动画 \r\n 风格：深色 Apple 风 + 3Blue1Brown + OpenAI 发布会 \r\n" },
                new StyleOption { Name = "科技科普-浅色", Description = " \r\n 类型：科技科普 / AI 原理动画 \r\n 风格：浅色 Apple 风 + 3Blue1Brown + OpenAI 发布会 \r\n" },
                new StyleOption { Name = "插画卡通", Description = "使用扁平化色彩、粗大的形状、活泼的弹跳或抖动动画。色调鲜艳（黄、红、绿、粉）。元素圆角分明，排版紧凑不拘泥于对称，动效夸张而富有弹性。" },
                new StyleOption { Name = "极简高冷", Description = "只有黑白灰为主色，搭配单一的高对比点缀色。动画风格极其平滑、缓慢（EaseInOut）。文字通常使用大留白，排版采用非常严谨的网格系统，极度干净。" }
            };
            CmbStyles.ItemsSource = styles;
            CmbStyles.SelectedIndex = 0;
        }

        private void ParseScriptJson(string json)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(json)) return;

                var jsonContent = json;
                if (jsonContent.Contains("```json"))
                {
                    var start = jsonContent.IndexOf("```json") + 7;
                    var end = jsonContent.LastIndexOf("```");
                    if (end > start)
                        jsonContent = jsonContent.Substring(start, end - start).Trim();
                }

                LstStoryboards.Items.Clear();
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var scenes = JsonSerializer.Deserialize<List<RemotionStoryboard>>(jsonContent, options);
                if (scenes != null)
                {
                    foreach (var scenee in scenes)
                    {
                        int id = scenee.SceneId;
                        string desc = scenee.SceneDescription ?? "";
                        decimal dur = scenee.Duration;

                        // 从数据库查询是否已有生成的记录，如果有则视为已完成
                        var existingRecord = _remotionRepo.GetByOverviewAndScene(CurrentOverviewId, id);
                        bool isCompleted = existingRecord != null && !string.IsNullOrWhiteSpace(existingRecord.GeneratedHtml);

                        // 如果数据库中存在记录，则直接使用数据库中的 RawJson
                        string rawJson = existingRecord?.RawJson;
                        if (string.IsNullOrWhiteSpace(rawJson))
                        {
                            // 否则，序列化当前的 scenee 对象
                            rawJson = JsonSerializer.Serialize(scenee, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
                        }

                        LstStoryboards.Items.Add(new StoryboardItem
                        {
                            Index = id,
                            Title = string.IsNullOrWhiteSpace(desc) ? $"第 {id} 镜" : desc,
                            TimeRange = $"持续 {dur} 秒",
                            RawJson = rawJson,
                            IsCompleted = isCompleted,
                            ReferenceHtml = isCompleted ? existingRecord.GeneratedHtml : null
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                // 解析失败时不报错，防止一开始文案非标准JSON的问题
                AppLogger.Error("解析分镜JSON失败: " + ex.Message, ex);
            }
        }

        private void LoadPrompts(int? selectedId = null)
        {
            var prompts = _promptRepo.GetByType(PromptType.GenerateRemotionHtml);
            CmbPrompts.ItemsSource = prompts;
            if (prompts.Count > 0)
            {
                if (selectedId.HasValue)
                {
                    foreach (Prompt item in CmbPrompts.Items)
                    {
                        if (item.Id == selectedId.Value)
                        {
                            CmbPrompts.SelectedItem = item;
                            break;
                        }
                    }
                }
                else
                {
                    CmbPrompts.SelectedIndex = 0;
                }
            }
            ToggleEditMode(false);
        }

        private void CmbPrompts_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbPrompts.SelectedItem is Prompt prompt && !_isEditingPrompt && !_isAddingPrompt)
            {
                _currentPrompt = prompt;
                TxtFullPrompt.Text = prompt.Content;
                //UpdateAnimationCheckBoxesState();
            }
        }

        private void UpdateAnimationCheckBoxesState()
        {
            if (TxtFullPrompt == null) return;
            string text = TxtFullPrompt.Text;

            _isUpdatingAnimationCheckBoxes = true;
            try
            {
                if (ChkAllowCenterArea != null) ChkAllowCenterArea.IsChecked = text.Contains(ChkAllowCenterArea.Content.ToString());
            }
            finally
            {
                _isUpdatingAnimationCheckBoxes = false;
            }

            UpdateCenterAreaLayout();
        }

        private void ToggleEditMode(bool isEditing)
        {
            if (isEditing)
            {
                CmbPrompts.Visibility = Visibility.Collapsed;
                TxtPromptTitle.Visibility = Visibility.Visible;
                StkActionButtons.Visibility = Visibility.Collapsed;
                StkEditButtons.Visibility = Visibility.Visible;

                if (CmbPrompts.SelectedItem is Prompt prompt)
                {
                    TxtFullPrompt.Text = prompt.Content;
                }
            }
            else
            {
                _isEditingPrompt = false;
                _isAddingPrompt = false;
                CmbPrompts.Visibility = Visibility.Visible;
                TxtPromptTitle.Visibility = Visibility.Collapsed;
                StkActionButtons.Visibility = Visibility.Visible;
                StkEditButtons.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnAddPrompt_Click(object sender, RoutedEventArgs e)
        {
            _isAddingPrompt = true;
            TxtPromptTitle.Text = "";
            TxtFullPrompt.Text = "";
            ToggleEditMode(true);
        }

        private void BtnEditPrompt_Click(object sender, RoutedEventArgs e)
        {
            if (CmbPrompts.SelectedItem is Prompt prompt)
            {
                _isEditingPrompt = true;
                TxtPromptTitle.Text = prompt.Title;
                ToggleEditMode(true);
            }
        }

        private void BtnDeletePrompt_Click(object sender, RoutedEventArgs e)
        {
            if (CmbPrompts.SelectedItem is Prompt prompt)
            {
                if (MessageBox.Show($"确定要删除提示词 '{prompt.Title}' 吗？", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    _promptRepo.Delete(prompt.Id);
                    LoadPrompts();
                }
            }
        }

        private void BtnSavePrompt_Click(object sender, RoutedEventArgs e)
        {
            string title = TxtPromptTitle.Text.Trim();
            string content = TxtFullPrompt.Text.Trim();

            if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(content))
            {
                MessageBox.Show("标题和内容不能为空！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_isAddingPrompt)
            {
                var newPrompt = new Prompt
                {
                    Title = title,
                    Type = PromptType.GenerateRemotionHtml,
                    Content = content
                };
                _promptRepo.Add(newPrompt);
                LoadPrompts(newPrompt.Id);
            }
            else if (_isEditingPrompt && _currentPrompt != null)
            {
                _currentPrompt.Title = title;
                _currentPrompt.Content = content;
                _promptRepo.Update(_currentPrompt);
                LoadPrompts(_currentPrompt.Id);
            }
        }

        private void BtnCancelPrompt_Click(object sender, RoutedEventArgs e)
        {
            ToggleEditMode(false);
        }

        private void AnimationPromptOption_Checked(object sender, RoutedEventArgs e)
        {
            AppendCheckedOptionToAnimationPrompt(sender as CheckBox);
            if (sender == ChkAllowCenterArea)
            {
                //UpdateCenterAreaLayout();
            }
        }

        private void AnimationPromptOption_Unchecked(object sender, RoutedEventArgs e)
        {
            RemoveUncheckedOptionFromAnimationPrompt(sender as CheckBox);
            if (sender == ChkAllowCenterArea)
            {
                UpdateCenterAreaLayout();
            }
        }

        private void AppendCheckedOptionToAnimationPrompt(CheckBox checkBox)
        {
            if (checkBox == null || TxtFullPrompt == null || _isEditingPrompt || _isAddingPrompt || _isUpdatingAnimationCheckBoxes) return;
            string optText = "\n- " + checkBox.Content.ToString();
            if (!TxtFullPrompt.Text.Contains(optText))
            {
                TxtFullPrompt.Text += optText;
            }
        }

        private void RemoveUncheckedOptionFromAnimationPrompt(CheckBox checkBox)
        {
            if (checkBox == null || TxtFullPrompt == null || _isEditingPrompt || _isAddingPrompt || _isUpdatingAnimationCheckBoxes) return;
            string optText = "\n- " + checkBox.Content.ToString();
            if (TxtFullPrompt.Text.Contains(optText))
            {
                TxtFullPrompt.Text = TxtFullPrompt.Text.Replace(optText, "");
            }
        }

        private void CmbStyles_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Nothing to do immediately when style changes
        }

        private void LstStoryboards_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            if (sender is ListBox listBox && e.Delta != 0)
            {
                var scrollViewer = GetDescendantByType<ScrollViewer>(listBox);
                if (scrollViewer != null)
                {
                    if (e.Delta > 0)
                        scrollViewer.LineUp();
                    else
                        scrollViewer.LineDown();
                    e.Handled = true;
                }
            }
        }

        private static T GetDescendantByType<T>(System.Windows.DependencyObject visual) where T : System.Windows.DependencyObject
        {
            if (visual == null)
                return null;

            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(visual); i++)
            {
                System.Windows.DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(visual, i);
                if (child is T t)
                    return t;

                T descendant = GetDescendantByType<T>(child);
                if (descendant != null)
                    return descendant;
            }
            return null;
        }

        private void BtnSendPrompt_Click(object sender, RoutedEventArgs e)
        {
            string promptInfo = TxtFullPrompt.Text.Trim();

            if (LstStoryboards.SelectedItem is not StoryboardItem selectedScene)
            {
                MessageBox.Show("请先从左侧分镜列表中选择一个需要生成的镜头！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(promptInfo))
            {
                MessageBox.Show("提示词说明不能为空！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 获取参考代码
            string referenceCode = "";
            foreach (StoryboardItem item in LstStoryboards.Items)
            {
                if (item.IsReference && item.IsCompleted)
                {
                    referenceCode = item.ReferenceHtml;
                    break; // 只要取一个
                }
            }

            // 获取选择的风格描述
            string styleDesc = "";
            if (CmbStyles.SelectedItem is StyleOption styleOption && styleOption.Name != "无")
            {
                styleDesc = $"本镜头所需要使用的视觉风格偏好要求如下：{styleOption.Description}";
            }

            // 替换占位符
            string fullMessage = promptInfo;
            if (fullMessage.Contains("{{input_content}}"))
            {
                fullMessage = fullMessage.Replace("{{input_content}}", selectedScene.RawJson ?? "");
            }
            if (fullMessage.Contains("{{reference_content}}"))
            {
                fullMessage = fullMessage.Replace("{{reference_content}}", string.IsNullOrWhiteSpace(referenceCode) ? "" : "参考以下html的风格、背景样式、字幕样式、镜头语言、入场动画和色调，保证视觉效果一致：【\r\n" + referenceCode + "\r\n】");
            }
            if (fullMessage.Contains("{{Resolution}}"))
            {
                string resStr = (CmbResolution.SelectedItem as ComboBoxItem)?.Content?.ToString();
                fullMessage = fullMessage.Replace("{{Resolution}}", resStr);
            }

            // 附加风格
            if (!string.IsNullOrWhiteSpace(styleDesc))
            {
                fullMessage += "\n\n" + styleDesc;
            }

            Clipboard.SetText(fullMessage);
            MessageBox.Show("动画生成提示词已成功复制到剪贴板，请到浏览器中粘贴即可。", "复制成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private CefSharp.Wpf.ChromiumWebBrowser _previewBrowser;

        private void CmbResolution_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateBrowserResolution();
        }

        private void UpdateBrowserResolution()
        {
            if (BrowserWrapper != null && CmbResolution != null)
            {
                string resStr = (CmbResolution.SelectedItem as ComboBoxItem)?.Content?.ToString();
                if (resStr != null && resStr.Contains("1080x1920")) { BrowserWrapper.Width = 1080; BrowserWrapper.Height = 1920; }
                else if (resStr != null && resStr.Contains("1080x1080")) { BrowserWrapper.Width = 1080; BrowserWrapper.Height = 1080; }
                else { BrowserWrapper.Width = 1920; BrowserWrapper.Height = 1080; }
            }
        }

        private void PreviewTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender == TabHtmlEdit)
            {
                TxtHtmlContent.Visibility = Visibility.Visible;
                if (PreviewViewbox != null)
                {
                    PreviewViewbox.Visibility = Visibility.Collapsed;
                }
            }
            else if (sender == TabHtmlPreview)
            {
                TxtHtmlContent.Visibility = Visibility.Collapsed;

                if (_previewBrowser == null)
                {
                    _previewBrowser = new CefSharp.Wpf.ChromiumWebBrowser();
                    BrowserWrapper.Children.Add(_previewBrowser);
                }

                PreviewViewbox.Visibility = Visibility.Visible;
                UpdateBrowserResolution();

                string html = TxtHtmlContent.Text;
                if (!string.IsNullOrWhiteSpace(html))
                {
                    // To render string as HTML in CefSharp
                    string base64EncodedHtml = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(html));
                    _previewBrowser.Address = $"data:text/html;base64,{base64EncodedHtml}";
                }
            }
        }

        private void BtnOpenInBrowser_Click(object sender, RoutedEventArgs e)
        {
            string html = TxtHtmlContent.Text;
            if (string.IsNullOrWhiteSpace(html))
            {
                MessageBox.Show("HTML 内容为空！");
                return;
            }

            try
            {
                string tempFile = Path.Combine(Path.GetTempPath(), $"Preview_{Guid.NewGuid():N}.html");
                File.WriteAllText(tempFile, html, new System.Text.UTF8Encoding(false));

                Process.Start(new ProcessStartInfo
                {
                    FileName = tempFile,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                AppLogger.Error("打开浏览器查看失败: " + ex.Message, ex);
                MessageBox.Show("打开浏览器查看失败：" + ex.Message);
            }
        }

        private void LstStoryboards_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstStoryboards.SelectedItem is StoryboardItem item)
            {
                _currentSelectedSceneId = item.Index;
                if (item.IsCompleted && !string.IsNullOrWhiteSpace(item.ReferenceHtml))
                {
                    TxtHtmlContent.Text = item.ReferenceHtml; 
                    if (!string.IsNullOrWhiteSpace(TxtHtmlContent.Text))
                    {
                        // To render string as HTML in CefSharp
                        if (_previewBrowser != null)
                        {
                            string base64EncodedHtml = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(TxtHtmlContent.Text));
                            _previewBrowser.Address = $"data:text/html;base64,{base64EncodedHtml}";
                        }
                    }
                }
                else
                {
                    TxtHtmlContent.Text = string.Empty;
                }
            }
        }

        private void BtnSaveHtml_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(CurrentOverviewId))
            {
                MessageBox.Show("概览ID不存在，请返回初始步骤重新生成！");
                return;
            }

            string html = TxtHtmlContent.Text;
            if (string.IsNullOrWhiteSpace(html))
            {
                MessageBox.Show("HTML 内容为空！");
                return;
            }

            try
            {
                _remotionRepo.UpdateStoryboardHtmlAndStatusByScene(CurrentOverviewId, _currentSelectedSceneId, html, "HtmlGenerated");

                // 实时更新当前列表中当前项的【已完成】状态，使其能显示出来
                foreach (StoryboardItem item in LstStoryboards.Items)
                {
                    if (item.Index == _currentSelectedSceneId)
                    {
                        item.IsCompleted = true;
                        item.ReferenceHtml = html; // 将当前保存的 html 同步给内存对象，用于后面提取样式参考
                        break;
                    }
                }

                MessageBox.Show($"第 {_currentSelectedSceneId} 镜的 HTML 保存成功！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex.Message, ex);
            }
        }
        private void BtnSaveAllHtml_Click(object sender, RoutedEventArgs e)
        {
            if (LstStoryboards.Items.Count == 0)
            {
                MessageBox.Show("分镜列表为空，没有可保存的HTML文件。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            dialog.Description = "请选择保存所有HTML文件的文件夹";
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string selectedPath = dialog.SelectedPath;
                int savedCount = 0;
                int failedCount = 0;

                foreach (StoryboardItem item in LstStoryboards.Items)
                {
                    if (item.IsCompleted && !string.IsNullOrWhiteSpace(item.ReferenceHtml))
                    {
                        try
                        {
                            string fileName = $"scene_{item.Index:D3}.html";
                            string filePath = Path.Combine(selectedPath, fileName);
                            File.WriteAllText(filePath, item.ReferenceHtml, new System.Text.UTF8Encoding(false));
                            savedCount++;
                        }
                        catch (Exception ex)
                        {
                            failedCount++;
                            AppLogger.Error($"保存分镜 {item.Index} 的HTML失败: {ex.Message}", ex);
                        }
                    }
                }

                MessageBox.Show($"保存完成！\n\n成功保存 {savedCount} 个文件。\n失败 {failedCount} 个。", "操作结果", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        private async void BtnExportVideo_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(CurrentOverviewId))
            {
                MessageBox.Show("没有生成任务！");
                return;
            }

            try
            {
                BtnExportVideo.IsEnabled = false;
                if (BtnBatchExportVideo != null) BtnBatchExportVideo.IsEnabled = false;

                // 准备输入参数
                bool isTransparent = ChkTransparentBg.IsChecked ?? false;
                string ext = isTransparent ? ".mov" : ".mp4";

                var configRepo = new ConfigRepository();
                string defaultDownloadDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AnimationGeneration");
                string baseDir = configRepo.GetConfig(AppConfigKeys.VideoDownloadPath, defaultDownloadDir);
                baseDir = Path.Combine(baseDir, "AnimationGeneration", CurrentOverviewId);

                if (!Directory.Exists(baseDir))
                {
                    Directory.CreateDirectory(baseDir);
                }
                string videoPath = Path.Combine(baseDir, $"Scene_{_currentSelectedSceneId}_{DateTime.Now:yyyyMMddHHmmss}{ext}");

                // 解析分辨率
                int width = 1920, height = 1080;
                string resStr = (CmbResolution.SelectedItem as ComboBoxItem)?.Content?.ToString();
                if (resStr != null && resStr.Contains("1080x1920")) { width = 1080; height = 1920; }
                else if (resStr != null && resStr.Contains("1080x1080")) { width = 1080; height = 1080; }

                // 解析FPS
                int fps = 30;
                string fpsStr = (CmbFps.SelectedItem as ComboBoxItem)?.Content?.ToString();
                if (fpsStr != null) int.TryParse(fpsStr, out fps);

                // 解析持续时间
                int durationMs = 5000;
                if (LstStoryboards.SelectedItem is StoryboardItem scene)
                {
                    // 假设 Duration 存在 TimeRange 中，例如 "持续 4 秒"
                    var numStr = System.Text.RegularExpressions.Regex.Match(scene.TimeRange, @"\d+").Value;
                    if (int.TryParse(numStr, out int seconds))
                    {
                        seconds = seconds == 0 ? 5 : seconds;
                        durationMs = seconds * 1000 + 3000;
                    }
                }

                string htmlContent = "";
                Dispatcher.Invoke(() =>
                {
                    htmlContent = TxtHtmlContent.Text;
                });

                await ExportVideoAsync(htmlContent, videoPath, width, height, fps, durationMs, isTransparent);

                // 执行完成，保存数据库状态
                _remotionRepo.UpdateStoryboardVideoSavePathByScene(CurrentOverviewId, _currentSelectedSceneId, videoPath, "VideoGenerated");

                MessageBox.Show($"导出录制成功！\n视频已保存到：{videoPath}", "导出成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex.Message, ex);
                MessageBox.Show("导出记录保存或录制失败：" + ex.Message);
            }
            finally
            {
                BtnExportVideo.IsEnabled = true;
                if (BtnBatchExportVideo != null) BtnBatchExportVideo.IsEnabled = true;
            }
        }
        
        private CancellationTokenSource _batchExportCts;

        private async void BtnBatchExportVideo_Click(object sender, RoutedEventArgs e)
        {
            if (_batchExportCts != null)
            {
                // Request cancellation
                _batchExportCts.Cancel();
                return;
            }

            if (string.IsNullOrEmpty(CurrentOverviewId))
            {
                MessageBox.Show("没有生成任务！");
                return;
            }

            try
            {
                _batchExportCts = new CancellationTokenSource();
                TxtBatchExport.Text = "停止批量录制...";
                IconBatchExport.Kind = MaterialDesignThemes.Wpf.PackIconKind.StopCircle;
                BtnBatchExportVideo.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 38, 38)); // Red color for stop
                BtnExportVideo.IsEnabled = false;

                // 准备输入参数
                bool isTransparent = ChkTransparentBg.IsChecked ?? false;
                string ext = isTransparent ? ".mov" : ".mp4";

                var configRepo = new ConfigRepository();
                string defaultDownloadDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AnimationGeneration");
                string baseDir = configRepo.GetConfig(AppConfigKeys.VideoDownloadPath, defaultDownloadDir);
                baseDir = Path.Combine(baseDir, "AnimationGeneration", CurrentOverviewId);

                if (!Directory.Exists(baseDir))
                {
                    Directory.CreateDirectory(baseDir);
                }

                // 解析分辨率
                int width = 1920, height = 1080;
                string resStr = (CmbResolution.SelectedItem as ComboBoxItem)?.Content?.ToString();
                if (resStr != null && resStr.Contains("1080x1920")) { width = 1080; height = 1920; }
                else if (resStr != null && resStr.Contains("1080x1080")) { width = 1080; height = 1080; }

                // 解析FPS
                int fps = 30;
                string fpsStr = (CmbFps.SelectedItem as ComboBoxItem)?.Content?.ToString();
                if (fpsStr != null) int.TryParse(fpsStr, out fps);

                int successCount = 0;
                int totalCount = LstStoryboards.Items.Count;

                for (int i = 0; i < totalCount; i++)
                {
                    if (_batchExportCts.Token.IsCancellationRequested)
                    {
                        break;
                    }

                    var scene = LstStoryboards.Items[i] as StoryboardItem;
                    if (scene == null) continue;

                    // 同步 UI 选中状态以方便直观查看
                    LstStoryboards.SelectedIndex = i;
                    
                    if (string.IsNullOrWhiteSpace(scene.ReferenceHtml))
                    {
                        AppLogger.Info($"第 {scene.Index} 镜未保存 HTML，跳过录制。");
                        continue;
                    }

                    int durationMs = 5000;
                    var numStr = System.Text.RegularExpressions.Regex.Match(scene.TimeRange, @"\d+").Value;
                    if (int.TryParse(numStr, out int seconds))
                    {
                        durationMs = seconds * 1000 + 3000;
                    }

                    string videoPath = Path.Combine(baseDir, $"Scene_{scene.Index}_{DateTime.Now:yyyyMMddHHmmss}{ext}");
                    string htmlContent = scene.ReferenceHtml;

                    AppLogger.Info($"开始批量录制第 {scene.Index} 镜...");

                    await ExportVideoAsync(htmlContent, videoPath, width, height, fps, durationMs, isTransparent, _batchExportCts.Token);

                    _remotionRepo.UpdateStoryboardVideoSavePathByScene(CurrentOverviewId, scene.Index, videoPath, "VideoGenerated");
                    successCount++;
                }

                if (_batchExportCts.Token.IsCancellationRequested)
                {
                    MessageBox.Show($"批量录制已取消。（成功导出 {successCount} 个视频）", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"批量录制完成！\n共成功导出 {successCount} 个视频。\n储存在:\n{baseDir}", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("批量导出记录保存或录制失败", ex);
                MessageBox.Show("批量导出过程出错：" + ex.Message);
            }
            finally
            {
                _batchExportCts?.Dispose();
                _batchExportCts = null;

                TxtBatchExport.Text = "一键批量录制";
                IconBatchExport.Kind = MaterialDesignThemes.Wpf.PackIconKind.AnimationPlay;
                BtnBatchExportVideo.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(217, 119, 6)); // #D97706
                BtnExportVideo.IsEnabled = true;
            }
        }
        public async Task ExportVideoAsync(
            string htmlContent,
            string outputPath,
            int width,
            int height,
            int fps,
            int durationMs,
            bool isTransparent,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(htmlContent))
                throw new InvalidOperationException("HTML 内容为空，无法录制。");

            if (width <= 0 || height <= 0 || fps <= 0 || durationMs <= 0)
                throw new ArgumentOutOfRangeException(nameof(durationMs), "视频尺寸、帧率和时长必须大于零。");

            await Task.Run(() => ExportVideoWithPlaywrightAsync(
                htmlContent, outputPath, width, height, fps, durationMs, isTransparent, cancellationToken), cancellationToken);
        }

        private static async Task ExportVideoWithPlaywrightAsync(
            string htmlContent,
            string outputPath,
            int width,
            int height,
            int fps,
            int durationMs,
            bool isTransparent,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (isTransparent)
            {
                htmlContent = EnsureTransparentPageBackground(htmlContent);
            }

            string ffmpegArgs = isTransparent
                ? $"-y -f image2pipe -vcodec png -framerate {fps} -i - -vf \"format=rgba,pad=ceil(iw/2)*2:ceil(ih/2)*2:0:0:color=0x00000000\" -c:v prores_ks -profile:v 4 -pix_fmt yuva444p10le \"{outputPath}\""
                : $"-y -f image2pipe -vcodec png -framerate {fps} -i - -vf \"pad=ceil(iw/2)*2:ceil(ih/2)*2\" -c:v libx264 -pix_fmt yuv420p \"{outputPath}\"";

            var startInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = ffmpegArgs,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var ffmpegProcess = Process.Start(startInfo)
                ?? throw new Exception("启动 FFmpeg 失败，请确保系统环境变量中配置了 FFmpeg，或将其放入程序运行目录。");

            var ffmpegErrorLog = new System.Text.StringBuilder();
            ffmpegProcess.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null) ffmpegErrorLog.AppendLine(e.Data);
            };
            ffmpegProcess.BeginErrorReadLine();

            try
            {
                using var playwright = await Playwright.CreateAsync();
                await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = true,
                    // 关键：强制使用完整版 Chromium（新无头模式）。
                    // Playwright 1.49+ 在 Headless=true 时默认启动 chrome-headless-shell（精简老内核），
                    // 它不解码 <video>（videoWidth=0、截图无视频画面），导出成片视频区域全黑。
                    // 实测对照：shell 内核面板区全黑，完整 Chromium 正常渲染视频帧。2026-09-15。
                    Channel = "chromium",
                    Args = new[]
                    {
                        "--no-sandbox",
                        "--disable-setuid-sandbox",
                        "--disable-dev-shm-usage",
                        "--disable-web-security",
                        // 不要加 --disable-gpu：无头 + 禁 GPU 时 <video> 解码帧不进入合成器输出，
                        // ScreenshotAsync 抓不到视频画面（只有 CSS 底色），导出成片视频区域全空。
                        "--mute-audio"
                    }
                });

                int totalFrames = Math.Max(1, (int)Math.Ceiling(durationMs / 1000.0 * fps));
                double frameDurationMs = 1000.0 / fps;
                // 每个工作页面独占时间轴；按未压缩帧大小限制并发，避免高分辨率导出占用过多内存。
                long frameBytes = (long)width * height * 4;
                int memoryLimitedWorkers = (int)Math.Clamp(128L * 1024 * 1024 / frameBytes, 1, 4);
                int workerCount = Math.Min(totalFrames, Math.Min(Environment.ProcessorCount, memoryLimitedWorkers));
                var pageTasks = new Task<IPage>[workerCount];
                for (int worker = 0; worker < workerCount; worker++)
                {
                    pageTasks[worker] = CreateExportPageAsync(browser, htmlContent, width, height, cancellationToken);
                }
                var pages = await Task.WhenAll(pageTasks);

                for (int batchStart = 0; batchStart < totalFrames; batchStart += workerCount)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int batchSize = Math.Min(workerCount, totalFrames - batchStart);
                    var frameTasks = new Task<byte[]>[batchSize];
                    for (int worker = 0; worker < batchSize; worker++)
                    {
                        double timeMs = (batchStart + worker) * frameDurationMs;
                        frameTasks[worker] = CaptureExportFrameAsync(pages[worker], timeMs, isTransparent, cancellationToken);
                    }

                    // WhenAll 的结果顺序与任务数组一致，不受截图完成先后影响。
                    // 每批最多缓存 workerCount 帧，且只有此处按帧号顺序写入 FFmpeg。
                    byte[][] frames = await Task.WhenAll(frameTasks);
                    foreach (byte[] pngFrame in frames)
                    {
                        await ffmpegProcess.StandardInput.BaseStream.WriteAsync(pngFrame, 0, pngFrame.Length, cancellationToken);
                    }
                }
            }
            catch
            {
                StopExportEncoder(ffmpegProcess);
                throw;
            }
            finally
            {
                try
                {
                    ffmpegProcess.StandardInput.Close();
                }
                catch
                {
                    // Ignore pipe close failures while surfacing the original result.
                }
            }

            try
            {
                await ffmpegProcess.WaitForExitAsync(cancellationToken);
            }
            catch
            {
                StopExportEncoder(ffmpegProcess);
                throw;
            }

            if (ffmpegProcess.ExitCode != 0)
            {
                string error = ffmpegErrorLog.ToString();
                AppLogger.Error($"FFmpeg exited with code {ffmpegProcess.ExitCode}. Log: {error}", null);
                throw new Exception($"FFmpeg 录制失败，退出码: {ffmpegProcess.ExitCode}。请检查日志。\n详细日志:\n{error}");
            }
        }

        private static void StopExportEncoder(Process process)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }
            catch (Exception ex)
            {
                AppLogger.Error("停止视频导出编码器失败。", ex);
            }
        }

        private static async Task<IPage> CreateExportPageAsync(
            Microsoft.Playwright.IBrowser browser, string htmlContent, int width, int height, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await browser.NewPageAsync(new BrowserNewPageOptions
            {
                ViewportSize = new ViewportSize { Width = width, Height = height },
                DeviceScaleFactor = 1
            });
            await page.SetContentAsync(htmlContent, new PageSetContentOptions { WaitUntil = WaitUntilState.Load });
            if (page.IsClosed)
                throw new InvalidOperationException("导出页面已关闭。");

            // 视频请求可能持续连接，不能使用 NetworkIdle 作为就绪条件。
            await WaitForVideosReadyAsync(page);
            if (await page.EvaluateAsync<bool>("() => !!document.fonts"))
                await page.EvaluateAsync("async () => { await document.fonts.ready; }");
            await FreezeCssAnimationsAsync(page);
            cancellationToken.ThrowIfCancellationRequested();
            return page;
        }

        private static async Task<byte[]> CaptureExportFrameAsync(
            IPage page, double timeMs, bool isTransparent, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await SeekCssAnimationsAsync(page, timeMs);
            await NotifyFrameTimeAsync(page, timeMs);
            // seek 完成不代表视频帧已进入合成器，仍需等待实际视频画面。
            await WaitForVideosRenderedAsync(page);
            cancellationToken.ThrowIfCancellationRequested();
            return await page.ScreenshotAsync(new PageScreenshotOptions
            {
                Type = ScreenshotType.Png,
                OmitBackground = isTransparent,
                FullPage = false
            });
        }

        private static Task FreezeCssAnimationsAsync(IPage page)
        {
            return page.EvaluateAsync("""
                () => {
                    const freeze = () => {
                        for (const animation of document.getAnimations({ subtree: true })) {
                            animation.pause();
                            animation.currentTime = 0;
                        }
                    };

                    freeze();
                    window.__noteflowFreezeAnimations = freeze;
                    document.fonts && document.fonts.ready && document.fonts.ready.then(freeze);
                }
                """);
        }

        private static Task SeekCssAnimationsAsync(IPage page, double timeMs)
        {
            return page.EvaluateAsync(
                """
                (timeMs) => {
                    const animations = document.getAnimations({ subtree: true });
                    for (const animation of animations) {
                        animation.pause();
                        animation.currentTime = timeMs;
                    }
                }
                """,
                timeMs);
        }

        private static Task NotifyFrameTimeAsync(IPage page, double timeMs)
        {
            return page.EvaluateAsync(
                """
                (timeMs) => {
                    window.__noteflowFrameTimeMs = timeMs;
                    window.dispatchEvent(new CustomEvent('noteflow:frame', { detail: { timeMs } }));
                }
                """,
                timeMs);
        }

        private static Task WaitForVideosReadyAsync(IPage page)
        {
            return page.EvaluateAsync(
                """
                () => {
                    const videos = Array.from(document.querySelectorAll('video'));
                    if (!videos.length) return;

                    return Promise.all(videos.map(video => new Promise((resolve, reject) => {
                        if (video.error) {
                            reject(new Error(`视频加载失败: ${video.error.message || video.error.code}`));
                            return;
                        }
                        if (video.readyState >= HTMLMediaElement.HAVE_METADATA) {
                            resolve();
                            return;
                        }

                        // preload="none" 的视频不会自行加载元数据（loadedmetadata 永不触发），
                        // 必须主动 load() 启动资源获取，否则等待必然超时。
                        if (video.preload === 'none') {
                            video.preload = 'metadata';
                            video.load();
                        }

                        const timeout = setTimeout(() => reject(new Error('等待视频元数据超时')), 10000);
                        const onReady = () => {
                            clearTimeout(timeout);
                            video.removeEventListener('loadedmetadata', onReady);
                            video.removeEventListener('error', onError);
                            resolve();
                        };
                        const onError = () => {
                            clearTimeout(timeout);
                            video.removeEventListener('loadedmetadata', onReady);
                            video.removeEventListener('error', onError);
                            reject(new Error(`视频加载失败: ${video.error?.message || video.error?.code || 'unknown'}`));
                        };
                        video.addEventListener('loadedmetadata', onReady, { once: true });
                        video.addEventListener('error', onError, { once: true });
                    })));
                }
                """);
        }

        // 等待当前帧的视频画面真正进入合成器。
        // 旧实现对所有 video 无差别等待 seeked + requestVideoFrameCallback，
        // 但 rVFC 只在「有新帧提交给合成器」时才触发。三种常见状态永远等不到 rVFC：
        //   1) 元素被 GSAP autoAlpha 隐藏（visibility:hidden，不参与合成）；
        //   2) 目标时间与当前时间同值（页面侧 syncVideos 同值短路，根本没发起 seek）；
        //   3) 目标时间被钳制（data-start 未到 / 已超过片尾，每帧目标恒定不变）。
        // 这些场景下每一帧都要吃满兜底超时（原 5s），是含视频分镜导出极慢的根因。
        // 新逻辑：按与页面 syncVideos 完全一致的公式计算每路视频的目标时间，
        // 同值 / 隐藏 / 移出视口 → 立即放行（本帧不会有新帧呈现，截图也不受影响）；
        // 真实 seek → 等 seeked + rVFC（可见视频 seek 后必有新帧，附短兜底防同帧静默）。
        private static Task WaitForVideosRenderedAsync(IPage page)
        {
            return page.EvaluateAsync(
                """
                () => {
                    const videos = Array.from(document.querySelectorAll('video'));
                    if (!videos.length) return;

                    // 新版页面模板在每次 noteflow:frame 时把本次同步 Promise 挂到
                    // __noteflowVideoSeekDone（页面侧最清楚哪路同值短路 / 隐藏跳过）。
                    // 存在则优先精确等待它；仍留兜底防页面 promise 悬挂拖死导出。
                    const sig = window.__noteflowVideoSeekDone;
                    if (sig && typeof sig.then === 'function') {
                        return Promise.race([sig, new Promise((r) => setTimeout(r, 3000))]);
                    }

                    // 与页面 syncVideos 完全一致的目标时间（含 data-start 偏移与片尾钳制）
                    const t = Math.max(0, Number(window.__noteflowFrameTimeMs || 0) / 1000);
                    const EPS = 0.002;

                    const targetOf = (v) => {
                        if (!isFinite(v.duration) || v.duration <= 0) return null;
                        const start = Number(v.dataset.start || 0);
                        let local = t - start;
                        if (local < 0) local = 0;
                        return Math.min(local, v.duration - 0.001);
                    };

                    // 隐藏（含祖先 visibility:hidden / opacity:0）或整体移出视口的元素
                    // 不参与合成、也不会出现在截图里，无需等待。
                    const isVisible = (v) => {
                        try {
                            if (typeof v.checkVisibility === 'function' &&
                                !v.checkVisibility({ checkOpacity: true, checkVisibilityCSS: true, opacityProperty: true, visibilityProperty: true, contentVisibilityAuto: true })) {
                                return false;
                            }
                            let n = v;
                            while (n && n instanceof Element) {
                                const s = getComputedStyle(n);
                                if (s.display === 'none' || s.visibility === 'hidden' || parseFloat(s.opacity) === 0) return false;
                                n = n.parentElement;
                            }
                            const r = v.getBoundingClientRect();
                            const vw = window.innerWidth, vh = window.innerHeight;
                            if (r.width <= 0 || r.height <= 0 || r.right <= 0 || r.bottom <= 0 || r.left >= vw || r.top >= vh) return false;
                            return true;
                        } catch (e) {
                            return true;
                        }
                    };

                    return Promise.all(videos.map(video => new Promise((resolve, reject) => {
                        if (video.error) {
                            reject(new Error(`视频解码失败: ${video.error.message || video.error.code}`));
                            return;
                        }
                        if (video.readyState < 1) { resolve(); return; }

                        const target = targetOf(video);
                        if (target === null) { resolve(); return; }
                        // 同帧目标且无进行中的 seek：页面侧已短路，本帧不会再有 seek。
                        // 例外：seek 若恰好在两次 CDP 求值间隙完成，帧已解码但可能尚未进合成器，
                        // 直接放行会截到上一帧 → 用带 30ms 兜底的单次 rVFC 等呈现；
                        // 静止视频无新帧可呈现时最多等 30ms，代价可控。
                        if (!video.seeking && Math.abs(video.currentTime - target) < EPS) {
                            if (typeof video.requestVideoFrameCallback === 'function') {
                                let done0 = false;
                                const fin0 = () => { if (!done0) { done0 = true; resolve(); } };
                                video.requestVideoFrameCallback(() => fin0());
                                setTimeout(fin0, 30);
                            } else {
                                requestAnimationFrame(() => requestAnimationFrame(() => resolve()));
                            }
                            return;
                        }
                        // 隐藏 / 出视口：本帧截图不可见，不必等呈现
                        if (!isVisible(video)) { resolve(); return; }

                        let settled = false;
                        let rvfcCap = null;
                        const finish = () => {
                            if (settled) return;
                            settled = true;
                            clearTimeout(timeout);
                            if (rvfcCap) clearTimeout(rvfcCap);
                            video.removeEventListener('seeked', onSeeked);
                            video.removeEventListener('error', onError);
                            resolve();
                        };
                        const timeout = setTimeout(() => finish(), 3000);
                        const onSeeked = () => {
                            if (typeof video.requestVideoFrameCallback === 'function') {
                                video.requestVideoFrameCallback(() => finish());
                                // seek 落在同一个视频帧上时（如 24fps 源对 30fps 导出）没有新帧可呈现，
                                // rVFC 不会触发，用短兜底放行。
                                rvfcCap = setTimeout(() => finish(), 150);
                            } else {
                                requestAnimationFrame(() => requestAnimationFrame(() => finish()));
                            }
                        };
                        const onError = () => {
                            if (settled) return;
                            settled = true;
                            clearTimeout(timeout);
                            if (rvfcCap) clearTimeout(rvfcCap);
                            reject(new Error(`视频解码失败: ${video.error?.message || video.error?.code || 'unknown'}`));
                        };
                        video.addEventListener('seeked', onSeeked, { once: true });
                        video.addEventListener('error', onError, { once: true });
                        if (!video.seeking) {
                            // 赋值后未进入 seeking 且时间已到位：seek 已在监听器挂载前完成
                            if (Math.abs(video.currentTime - target) < EPS) finish();
                            else onSeeked();
                        }
                    })));
                }
                """);
        }

        private static string EnsureTransparentPageBackground(string htmlContent)
        {
            const string transparentStyle = """
                <style id="noteflow-transparent-export-style">
                    html, body {
                        background: transparent !important;
                        background-color: transparent !important;
                    }
                </style>
                """;

            int headEndIndex = htmlContent.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
            if (headEndIndex >= 0)
            {
                return htmlContent.Insert(headEndIndex, transparentStyle);
            }

            return transparentStyle + htmlContent;
        }

        private bool _isPreviewZoomed = false;

        private void UpdateCenterAreaLayout()
        {
            if (_isPreviewZoomed) return;

            bool allowCenterArea = ChkAllowCenterArea?.IsChecked == true;
            PreviewColumn.Width = allowCenterArea
                ? new GridLength(1.8, GridUnitType.Star)
                : new GridLength(1.5, GridUnitType.Star);
        }

        private void BtnToggleZoom_Click(object sender, RoutedEventArgs e)
        {
            _isPreviewZoomed = !_isPreviewZoomed;

            if (_isPreviewZoomed)
            {
                // 放大
                LeftPanel.Width = new GridLength(0.8, GridUnitType.Star);
                PreviewColumn.Width = new GridLength(2.2, GridUnitType.Star);
                ZoomIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.ArrowCollapse;
                ExportSettings.Visibility = Visibility.Collapsed;
            }
            else
            {
                // 还原
                LeftPanel.Width = new GridLength(0.8, GridUnitType.Star);
                ZoomIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.ArrowExpand;
                ExportSettings.Visibility = Visibility.Visible;
                UpdateCenterAreaLayout();
            }
        }

        private static T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parentObject = System.Windows.Media.VisualTreeHelper.GetParent(child);

            if (parentObject == null) return null;

            T parent = parentObject as T;
            if (parent != null)
                return parent;
            else
                return FindParent<T>(parentObject);
        }
    }
}
