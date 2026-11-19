using Microsoft.Win32;
using NoteFlow.Studio.Model.Config;
using NoteFlow.Studio.Model;
using NoteFlow.Studio.Repository;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System;

namespace NoteFlow.Studio.Views.VideoGeneration
{
    public partial class CleanTextTabPanel : UserControl
    {
        private PromptRepository _promptRepo;
        private bool _isEditingPrompt = false;
        private bool _isAddingPrompt = false;
        private Prompt _currentPrompt;

        public event Action<string> ExecuteScriptRequested;
        public event Action NextTabRequested;
        public event Action NewRemotionRequested;

        private string _sourceTextPath = string.Empty;
        public string SourceTextPath
        {
            get { return _sourceTextPath; }
            set
            {
                _sourceTextPath = value ?? string.Empty;
                UpdateSelectedFileButton();
            }
        }

        public string CleanedText
        {
            get { return TxtCleanedResult.Text; }
            set { TxtCleanedResult.Text = value; }
        }

        public string CurrentOverviewId { get; set; }
        private readonly RemotionRepository _remotionRepo = new RemotionRepository();

        public CleanTextTabPanel()
        {
            InitializeComponent();
            _promptRepo = new PromptRepository();
            Loaded += CleanTextTabPanel_Loaded;
        }

        private void CleanTextTabPanel_Loaded(object sender, RoutedEventArgs e)
        {
            LoadPrompts();
            UpdateSelectedFileButton();
        }

        private void UpdateSelectedFileButton()
        {
            if (BtnSelectFile == null) return;
            BtnSelectFile.Content = string.IsNullOrWhiteSpace(SourceTextPath)
                ? "导入本地文件..."
                : SourceTextPath;
        }

        public void ResetForNewRemotion(string overviewId)
        {
            CurrentOverviewId = overviewId;
            SourceTextPath = string.Empty;
            TxtOriginalContent.Text = string.Empty;
            TxtCleanedResult.Text = string.Empty;
            ToggleEditMode(false);
        }

        private void LoadPrompts(int? selectedId = null)
        {
            var prompts = _promptRepo.GetByType(PromptType.CleanText);
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
                UpdateCheckBoxesState();
            }
        }

        private bool _isUpdatingCheckBoxes = false;

        private void UpdateCheckBoxesState()
        {
            if (TxtFullPrompt == null) return;
            string text = TxtFullPrompt.Text;
            
            _isUpdatingCheckBoxes = true;
            try
            {
                if (ChkOpt1 != null) ChkOpt1.IsChecked = text.Contains(ChkOpt1.Content.ToString());
                if (ChkOpt2 != null) ChkOpt2.IsChecked = text.Contains(ChkOpt2.Content.ToString());
                if (ChkOpt3 != null) ChkOpt3.IsChecked = text.Contains(ChkOpt3.Content.ToString());
                if (ChkOpt4 != null) ChkOpt4.IsChecked = text.Contains(ChkOpt4.Content.ToString());
            }
            finally
            {
                _isUpdatingCheckBoxes = false;
            }
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
                    Type = PromptType.CleanText,
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

        private void PromptOption_Checked(object sender, RoutedEventArgs e) { AppendCheckedOptionToPrompt(sender as CheckBox); }
        private void PromptOption_Unchecked(object sender, RoutedEventArgs e) { RemoveUncheckedOptionFromPrompt(sender as CheckBox); }

        private void AppendCheckedOptionToPrompt(CheckBox checkBox)
        {
            if (checkBox == null || TxtFullPrompt == null || _isEditingPrompt || _isAddingPrompt || _isUpdatingCheckBoxes) return;
            string optText = "\n- " + checkBox.Content.ToString();
            if (!TxtFullPrompt.Text.Contains(optText))
            {
                TxtFullPrompt.Text += optText;
            }
        }

        private void RemoveUncheckedOptionFromPrompt(CheckBox checkBox)
        {
            if (checkBox == null || TxtFullPrompt == null || _isEditingPrompt || _isAddingPrompt || _isUpdatingCheckBoxes) return;
            string optText = "\n- " + checkBox.Content.ToString();
            if (TxtFullPrompt.Text.Contains(optText))
            {
                TxtFullPrompt.Text = TxtFullPrompt.Text.Replace(optText, "");
            }
        }

        private void BtnStartClean_Click(object sender, RoutedEventArgs e)
        {
            string promptInfo = TxtFullPrompt.Text.Trim();
            string originalText = TxtOriginalContent.Text.Trim();

            if (string.IsNullOrEmpty(promptInfo) || string.IsNullOrEmpty(originalText))
            {
                MessageBox.Show("提示词说明和原文来源不能为空！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string fullMessage = promptInfo.Contains("{{input_content}}") 
                ? promptInfo.Replace("{{input_content}}", originalText)
                : $"{promptInfo}\n\n文案为：【\r\n{originalText}\r\n】";
            
            Clipboard.SetText(fullMessage);
            MessageBox.Show("提示词已成功复制到剪贴板，请到浏览器中粘贴即可。", "复制成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnSelectFile_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                InitialDirectory = AppConfigKeys.ObsidianRootPath,
                Filter = "Text files (*.txt)|Markdown files (*.md)|*.md|*.txt|All files (*.*)|*.*"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                SourceTextPath = openFileDialog.FileName;
                TxtOriginalContent.Text = File.ReadAllText(openFileDialog.FileName);
            }
        }

        private void BtnNewRemotion_Click(object sender, RoutedEventArgs e)
        {
            NewRemotionRequested?.Invoke();
        }

        private void BtnReadClipboard_Click(object sender, RoutedEventArgs e)
        {
            if (Clipboard.ContainsText()) TxtCleanedResult.Text = Clipboard.GetText();
        }

        private void BtnGoToScript_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 先在当前组件存储记录
                _remotionRepo.SaveOrUpdateOverview(new RemotionOverview
                {
                    Id = CurrentOverviewId,
                    SourceTextPath = this.SourceTextPath,
                    CleanedText = this.CleanedText
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("保存概览数据失败: " + ex.Message);
            }

            NextTabRequested?.Invoke();
        }
    }
}
