using NoteFlow.Studio.Model.Config;
using NoteFlow.Studio.Model;
using NoteFlow.Studio.Repository;
using System.Windows;
using System.Windows.Controls;
using System;
using System.Collections.ObjectModel;
using NoteFlow.Studio.Infrastructure.Logging;
using NoteFlow.Studio.Service.Providers;

namespace NoteFlow.Studio.Views.VideoGeneration
{
    public class AiChunkItem
    {
        public string Text { get; set; }
    }

    public partial class ScriptGenerationTabPanel : UserControl
    {
        private PromptRepository _promptRepo;
        private bool _isEditingScriptPrompt = false;
        private bool _isAddingScriptPrompt = false;
        private Prompt _currentScriptPrompt;
        private readonly LLMService _llmService = new LLMService();
        public ObservableCollection<AiChunkItem> AiChunks { get; set; } = new ObservableCollection<AiChunkItem>();

        public event Action<string> ExecuteScriptRequested;
        public event Action NextTabRequested;

        public string ScriptCleanedText
        {
            get { return TxtScriptCleanedText.Text; }
            set { TxtScriptCleanedText.Text = value; }
        }

        public string ScriptResultText
        {
            get { return TxtScriptResult.Text; }
            set { TxtScriptResult.Text = value; }
        }

        public string CurrentOverviewId { get; set; }
        private readonly RemotionRepository _remotionRepo = new RemotionRepository();

        public ScriptGenerationTabPanel()
        {
            InitializeComponent();
            _promptRepo = new PromptRepository();
            LstAiChunks.ItemsSource = AiChunks;
            Loaded += ScriptGenerationTabPanel_Loaded;
        }

        private void UpdateEstimateText()
        {
            if (TbChunkEstimate == null || TxtScriptCleanedText == null) return;
            string text = TxtScriptCleanedText.Text ?? "";
            int charCount = text.Length;
            int blocks = 1;
            if (SldMaxWords != null && SldMaxWords.Value > 0)
            {
                blocks = (int)Math.Ceiling((double)charCount / SldMaxWords.Value);
                if (blocks == 0) blocks = 1;
            }
            TbChunkEstimate.Text = $"预计分为 {blocks} 块 (共 {charCount} 字)";
        }

        private void TxtScriptCleanedText_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateEstimateText();
        }

        private void RbWords_Checked(object sender, RoutedEventArgs e)
        {
            if (SldMaxWords == null) return;
            if (RbWords200?.IsChecked == true) SldMaxWords.Value = 200;
            else if (RbWords500?.IsChecked == true) SldMaxWords.Value = 500;
            else if (RbWords800?.IsChecked == true) SldMaxWords.Value = 800;
            else if (RbWords1000?.IsChecked == true) SldMaxWords.Value = 1000;
            
            UpdateEstimateText();
        }

        private void SldMaxWords_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (RbWordsCustom != null && !RbWordsCustom.IsChecked.GetValueOrDefault())
            {
                if (e.NewValue != 200 && e.NewValue != 500 && e.NewValue != 800 && e.NewValue != 1000)
                {
                    RbWordsCustom.IsChecked = true;
                }
            }
            UpdateEstimateText();
        }

        private void BtnToggleChunkPanel_Click(object sender, RoutedEventArgs e)
        {
            if (StkChunkPanelContent == null || IconChunkPanelToggle == null) return;

            bool isExpanded = StkChunkPanelContent.Visibility == Visibility.Visible;
            StkChunkPanelContent.Visibility = isExpanded ? Visibility.Collapsed : Visibility.Visible;
            IconChunkPanelToggle.Kind = isExpanded
                ? MaterialDesignThemes.Wpf.PackIconKind.ChevronDown
                : MaterialDesignThemes.Wpf.PackIconKind.ChevronUp;
        }

        private async void BtnInvokeAiChunking_Click(object sender, RoutedEventArgs e)
        {
            string fullText = TxtScriptCleanedText.Text.Trim();
            if (string.IsNullOrEmpty(fullText))
            {
                MessageBox.Show("请先输入需要清洗分块的文案！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string method = "字数切割";
            if (RbChunkPara?.IsChecked == true) method = "段落切割";
            else if (RbChunkCustom?.IsChecked == true) method = "自定义分块";

            int maxWords = SldMaxWords != null ? (int)SldMaxWords.Value : 500;

            try
            {
                var mainWindow = Application.Current.MainWindow as MainWindow;
                if (mainWindow?.CmbGlobalModel?.SelectedItem is AiModel selectedModel)
                {
                    BtnInvokeAiChunking.IsEnabled = false;
                    BtnInvokeAiChunking.Content = "正在分块中...";
                    var chunks = await _llmService.AiChunkingAsync(selectedModel, fullText, method, maxWords);
                    AiChunks.Clear();
                    foreach (var chunk in chunks)
                    {
                        AiChunks.Add(new AiChunkItem { Text = chunk });
                    }
                }
            }
            finally
            {
                BtnInvokeAiChunking.IsEnabled = true;
                BtnInvokeAiChunking.Content = "重新分块";
            }
        }

        private void BtnAddChunk_Click(object sender, RoutedEventArgs e)
        {
            AiChunks.Add(new AiChunkItem { Text = "新分块..." });
        }

        private void BtnClearChunks_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("确定要清空所有分块吗？", "清空确认", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                AiChunks.Clear();
            }
        }

        private void BtnDeleteChunk_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is AiChunkItem item)
            {
                AiChunks.Remove(item);
            }
        }

        private void ScriptGenerationTabPanel_Loaded(object sender, RoutedEventArgs e)
        {
            LoadScriptPrompts();
        }

        public void ResetForNewRemotion(string overviewId)
        {
            CurrentOverviewId = overviewId;
            ScriptCleanedText = string.Empty;
            ScriptResultText = string.Empty;
            AiChunks.Clear();
            RbChunkWord.IsChecked = true;
            RbWords500.IsChecked = true;
            SldMaxWords.Value = 500;
            ToggleScriptEditMode(false);
            UpdateEstimateText();
        }

        private void LoadScriptPrompts(int? selectedId = null)
        {
            var prompts = _promptRepo.GetByType(PromptType.GenerateStoryboard);
            CmbScriptPrompts.ItemsSource = prompts;
            if (prompts.Count > 0)
            {
                if (selectedId.HasValue)
                {
                    foreach (Prompt item in CmbScriptPrompts.Items)
                    {
                        if (item.Id == selectedId.Value)
                        {
                            CmbScriptPrompts.SelectedItem = item;
                            break;
                        }
                    }
                }
                else
                {
                    CmbScriptPrompts.SelectedIndex = 0;
                }
            }
            ToggleScriptEditMode(false);
        }

        private void CmbScriptPrompts_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbScriptPrompts.SelectedItem is Prompt prompt && !_isEditingScriptPrompt && !_isAddingScriptPrompt)
            {
                _currentScriptPrompt = prompt;
                TxtScriptFullPrompt.Text = prompt.Content;
                UpdateScriptCheckBoxesState();
            }
        }

        private bool _isUpdatingScriptCheckBoxes = false;

        private void UpdateScriptCheckBoxesState()
        {
            if (TxtScriptFullPrompt == null) return;
            string text = TxtScriptFullPrompt.Text;
            
            _isUpdatingScriptCheckBoxes = true;
            try
            {
                if (ChkScriptOpt1 != null) ChkScriptOpt1.IsChecked = text.Contains(ChkScriptOpt1.Content.ToString());
                if (ChkScriptOpt2 != null) ChkScriptOpt2.IsChecked = text.Contains(ChkScriptOpt2.Content.ToString());
                if (ChkScriptOpt3 != null) ChkScriptOpt3.IsChecked = text.Contains(ChkScriptOpt3.Content.ToString());
                if (ChkScriptOpt4 != null) ChkScriptOpt4.IsChecked = text.Contains(ChkScriptOpt4.Content.ToString());
            }
            finally
            {
                _isUpdatingScriptCheckBoxes = false;
            }
        }

        private void ToggleScriptEditMode(bool isEditing)
        {
            if (isEditing)
            {
                CmbScriptPrompts.Visibility = Visibility.Collapsed;
                TxtScriptPromptTitle.Visibility = Visibility.Visible;
                StkScriptActionButtons.Visibility = Visibility.Collapsed;
                StkScriptEditButtons.Visibility = Visibility.Visible;

                if (CmbScriptPrompts.SelectedItem is Prompt prompt)
                {
                    TxtScriptFullPrompt.Text = prompt.Content;
                }
            }
            else
            {
                _isEditingScriptPrompt = false;
                _isAddingScriptPrompt = false;
                CmbScriptPrompts.Visibility = Visibility.Visible;
                TxtScriptPromptTitle.Visibility = Visibility.Collapsed;
                StkScriptActionButtons.Visibility = Visibility.Visible;
                StkScriptEditButtons.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnAddScriptPrompt_Click(object sender, RoutedEventArgs e)
        {
            _isAddingScriptPrompt = true;
            TxtScriptPromptTitle.Text = "";
            TxtScriptFullPrompt.Text = "";
            ToggleScriptEditMode(true);
        }

        private void BtnEditScriptPrompt_Click(object sender, RoutedEventArgs e)
        {
            if (CmbScriptPrompts.SelectedItem is Prompt prompt)
            {
                _isEditingScriptPrompt = true;
                TxtScriptPromptTitle.Text = prompt.Title;
                ToggleScriptEditMode(true);
            }
        }

        private void BtnDeleteScriptPrompt_Click(object sender, RoutedEventArgs e)
        {
            if (CmbScriptPrompts.SelectedItem is Prompt prompt)
            {
                if (MessageBox.Show($"确定要删除提示词 '{prompt.Title}' 吗？", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    _promptRepo.Delete(prompt.Id);
                    LoadScriptPrompts();
                }
            }
        }

        private void BtnSaveScriptPrompt_Click(object sender, RoutedEventArgs e)
        {
            string title = TxtScriptPromptTitle.Text.Trim();
            string content = TxtScriptFullPrompt.Text.Trim();

            if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(content))
            {
                MessageBox.Show("标题和内容不能为空！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_isAddingScriptPrompt)
            {
                var newPrompt = new Prompt
                {
                    Title = title,
                    Type = PromptType.GenerateStoryboard,
                    Content = content
                };
                _promptRepo.Add(newPrompt);
                LoadScriptPrompts(newPrompt.Id);
            }
            else if (_isEditingScriptPrompt && _currentScriptPrompt != null)
            {
                _currentScriptPrompt.Title = title;
                _currentScriptPrompt.Content = content;
                _promptRepo.Update(_currentScriptPrompt);
                LoadScriptPrompts(_currentScriptPrompt.Id);
            }
        }

        private void BtnCancelScriptPrompt_Click(object sender, RoutedEventArgs e)
        {
            ToggleScriptEditMode(false);
        }

        private void ScriptPromptOption_Checked(object sender, RoutedEventArgs e) { AppendCheckedOptionToScriptPrompt(sender as CheckBox); }
        private void ScriptPromptOption_Unchecked(object sender, RoutedEventArgs e) { RemoveUncheckedOptionFromScriptPrompt(sender as CheckBox); }

        private void AppendCheckedOptionToScriptPrompt(CheckBox checkBox)
        {
            if (checkBox == null || TxtScriptFullPrompt == null || _isEditingScriptPrompt || _isAddingScriptPrompt || _isUpdatingScriptCheckBoxes) return;
            string optText = "\n- " + checkBox.Content.ToString();
            if (!TxtScriptFullPrompt.Text.Contains(optText))
            {
                TxtScriptFullPrompt.Text += optText;
            }
        }

        private void RemoveUncheckedOptionFromScriptPrompt(CheckBox checkBox)
        {
            if (checkBox == null || TxtScriptFullPrompt == null || _isEditingScriptPrompt || _isAddingScriptPrompt || _isUpdatingScriptCheckBoxes) return;
            string optText = "\n- " + checkBox.Content.ToString();
            if (TxtScriptFullPrompt.Text.Contains(optText))
            {
                TxtScriptFullPrompt.Text = TxtScriptFullPrompt.Text.Replace(optText, "");
            }
        }

        private void BtnStartGenerateScript_Click(object sender, RoutedEventArgs e)
        {
            string promptInfo = TxtScriptFullPrompt.Text.Trim();
            string textContext = TxtScriptCleanedText.Text.Trim();

            if (LstAiChunks.SelectedItem is AiChunkItem selectedChunk && !string.IsNullOrWhiteSpace(selectedChunk.Text))
            {
                textContext = selectedChunk.Text.Trim();
            }

            if (string.IsNullOrEmpty(promptInfo) || string.IsNullOrEmpty(textContext))
            {
                MessageBox.Show("分镜脚本提示词说明和需要生成的文案不能为空！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string fullMessage = promptInfo.Contains("{{input_content}}")
                ? promptInfo.Replace("{{input_content}}", textContext)
                : $"{promptInfo}\n\n清洗后的文案为：【\r\n{textContext}\r\n】";

            Clipboard.SetText(fullMessage);
            MessageBox.Show("分镜脚本提示词已成功复制到剪贴板，请到浏览器中粘贴即可。", "复制成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnReadScriptClipboard_Click(object sender, RoutedEventArgs e)
        {
            if (Clipboard.ContainsText()) TxtScriptResult.Text = Clipboard.GetText();
        }

        private void BtnGoToAnimation_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(CurrentOverviewId))
            {
                // 保存概览数据 (更新脚本内容)
                var overview = _remotionRepo.GetOverview(CurrentOverviewId);
                if (overview != null)
                {
                    overview.StoryboardScript = ScriptResultText;
                    _remotionRepo.SaveOrUpdateOverview(overview);
                }
                else
                {
                    // 如果跨步则补齐初始化
                    _remotionRepo.SaveOrUpdateOverview(new RemotionOverview
                    {
                        Id = CurrentOverviewId,
                        StoryboardScript = ScriptResultText
                    });
                }

                // 解析分镜 JSON 并保存到 RemotionStoryboard
                try
                {
                    var jsonContent = ScriptResultText;
                    if (!string.IsNullOrWhiteSpace(jsonContent))
                    {
                        if (jsonContent.Contains("```json"))
                        {
                            var start = jsonContent.IndexOf("```json") + 7;
                            var end = jsonContent.LastIndexOf("```");
                            if (end > start)
                                jsonContent = jsonContent.Substring(start, end - start).Trim();
                        }
                        
                        var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        var scenes = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<RemotionStoryboard>>(jsonContent, options);
                        
                        // 再次序列化以获得原始 JSON 字符串的列表
                        var sceneJsonElements = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<System.Text.Json.JsonElement>>(jsonContent, options);

                        if (scenes != null && sceneJsonElements != null && scenes.Count == sceneJsonElements.Count)
                        {
                            for (int i = 0; i < scenes.Count; i++)
                            {
                                var scene = scenes[i];
                                var sceneJson = sceneJsonElements[i].GetRawText();

                                var existingRecord = _remotionRepo.GetByOverviewAndScene(CurrentOverviewId, scene.SceneId);
                                if (existingRecord != null)
                                {
                                    // 更新现有记录
                                    existingRecord.SceneDescription = scene.SceneDescription;
                                    existingRecord.AnimationEffect = scene.AnimationEffect;
                                    existingRecord.Subtitles = scene.Subtitles;
                                    existingRecord.Duration = scene.Duration;
                                    existingRecord.BgmSoundtrack = scene.BgmSoundtrack;
                                    existingRecord.RawJson = sceneJson; // 更新 RawJson
                                    _remotionRepo.SaveStoryboard(existingRecord);
                                }
                                else
                                {
                                    // 创建新记录
                                    scene.Id = Guid.NewGuid().ToString();
                                    scene.OverviewId = CurrentOverviewId;
                                    scene.RawJson = sceneJson; // 设置 RawJson
                                    _remotionRepo.SaveStoryboard(scene);
                                }
                            }
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    AppLogger.Error(ex.Message, ex);
                }
            }

            NextTabRequested?.Invoke();
        }
    }
}
