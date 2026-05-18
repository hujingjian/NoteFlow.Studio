using Markdig;
using NoteFlow.Studio.Model.Config;
using NoteFlow.Studio.Infrastructure.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace NoteFlow.Studio.Views
{
    public partial class AddToKnowledgeBaseView
    {
        private async void BtnGenerateMarkdown_Click(object sender, RoutedEventArgs e)
        {
            var mainWindow = Application.Current.MainWindow as MainWindow;
            if (mainWindow?.CmbGlobalModel?.SelectedItem is AiModel selectedModel)
            {
                BtnGenerateMarkdown.IsEnabled = false;
                BtnArchive.IsEnabled = false;

                // Sync the main cancel task button state so user can cancel AI generation
                bool wasNotRunning = !_isTaskRunning;
                if (wasNotRunning)
                {
                    _isTaskRunning = true;
                    _globalCts = new System.Threading.CancellationTokenSource();
                    BtnToggleTask.Content = "取消任务";
                    BtnToggleTask.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#EF4444"));
                }

                TxtMarkdownEditor.Text = "";
                MarkdownViewer.NavigateToString(" ");

                MarkdownViewer.Visibility = Visibility.Collapsed;
                TxtMarkdownEditor.Visibility = Visibility.Visible;
                BtnEditMarkdown.Visibility = Visibility.Collapsed;
                BtnSaveMarkdown.Visibility = Visibility.Visible;
                ResultTabControl.SelectedIndex = 1;

                string ocrText = TxtOcrResult.Text;
                string pageUrl = _pageUrl; 
                if (string.IsNullOrEmpty(pageUrl))
                {
                    pageUrl = mainWindow.GetCurrentBrowserUrl(); // fallback
                }

                try
                {
                    //string markdown = await _llmService.GenerateMarkdownAsync(selectedModel, ocrText, videoUrl);
                    //TxtMarkdownEditor.Text = markdown;
                    //RenderMarkdown(markdown);
                    
                    var sb = new StringBuilder();
                    var lastUpdate = DateTime.Now;

                    await foreach (var chunk in _llmService.GenerateMarkdownStreamAsync(selectedModel, ocrText, pageUrl))
                    {
                        if (_globalCts != null && _globalCts.IsCancellationRequested)
                        {
                            TxtStep3Desc.Text = "已取消";
                            BtnGenerateMarkdown.IsEnabled = true;
                            return;
                        }

                        sb.Append(chunk);

                        // 节流：每2秒钟更新一次UI
                        if ((DateTime.Now - lastUpdate).TotalSeconds >= 2)
                        {
                            TxtMarkdownEditor.Text = sb.ToString();
                            TxtMarkdownEditor.ScrollToEnd();
                            lastUpdate = DateTime.Now;
                        }
                    }

                    // 循环结束时，强制更新最后一次
                    TxtMarkdownEditor.Text = sb.ToString();
                    TxtMarkdownEditor.ScrollToEnd();
                    
                    RenderMarkdown(TxtMarkdownEditor.Text);

                    MarkdownViewer.Visibility = Visibility.Visible;
                    TxtMarkdownEditor.Visibility = Visibility.Collapsed;
                    BtnEditMarkdown.Visibility = Visibility.Visible;
                    BtnSaveMarkdown.Visibility = Visibility.Collapsed;

                    // 提取YAML中的title
                    string generatedMarkdown = TxtMarkdownEditor.Text;
                    string title = "未命名";
                    var lines = generatedMarkdown.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    bool inYaml = false;
                    foreach (var line in lines)
                    {
                        if (line.Trim() == "---")
                        {
                            inYaml = !inYaml;
                            continue;
                        }
                        if (inYaml && line.StartsWith("title:"))
                        {
                            title = line.Substring("title:".Length).Trim().Trim('"', '\'');
                            break;
                        }
                    }

                    // 移除无效文件名字符
                    foreach (var c in Path.GetInvalidFileNameChars())
                    {
                        title = title.Replace(c.ToString(), "");
                    }

                    if (!string.IsNullOrEmpty(title))
                    {
                        TxtFileName.Text = title + ".md";
                        TxtStorageLocation.Text = title;
                    }

                    // 更新字数统计
                    int wordCount = ocrText.Length;
                    int markdownBytes = Encoding.UTF8.GetByteCount(generatedMarkdown);
                    int charCount = generatedMarkdown.Length;

                    TxtWordCount.Text = $"{wordCount} 字";
                    TxtMarkdownSize.Text = $"{Math.Round(markdownBytes / 1024.0, 2)} KB";
                    TxtCharCount.Text = $"{charCount}";

                    ResultTabControl.SelectedIndex = 1; // Switch to Markdown tab

                    IconStep3.Kind = MaterialDesignThemes.Wpf.PackIconKind.CheckCircle;
                    IconStep3.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 197, 94)); // #22C55E
                    TxtStep3Desc.Text = "完成";
                    BtnArchive.IsEnabled = true;
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"生成Markdown失败: {ex.Message}", ex);
                    MessageBox.Show($"生成Markdown失败: {ex.Message}");
                    TxtStep3Desc.Text = "生成失败";
                }
                finally
                {
                    BtnGenerateMarkdown.IsEnabled = true;
                    if (wasNotRunning && _isTaskRunning)
                    {
                        _isTaskRunning = false;
                        BtnToggleTask.Content = "开始任务";
                        BtnToggleTask.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#414DE5"));
                    }
                }
            }
            else
            {
                MessageBox.Show("请先在右下角选择一个AI模型。");
            }
        }

        private void BtnEditMarkdown_Click(object sender, RoutedEventArgs e)
        {
            MarkdownViewer.Visibility = Visibility.Collapsed;
            TxtMarkdownEditor.Visibility = Visibility.Visible;
            BtnEditMarkdown.Visibility = Visibility.Collapsed;
            BtnSaveMarkdown.Visibility = Visibility.Visible;
        }

        private void BtnSaveMarkdown_Click(object sender, RoutedEventArgs e)
        {
            MarkdownViewer.Visibility = Visibility.Visible;
            TxtMarkdownEditor.Visibility = Visibility.Collapsed;
            BtnEditMarkdown.Visibility = Visibility.Visible;
            BtnSaveMarkdown.Visibility = Visibility.Collapsed;
            RenderMarkdown(TxtMarkdownEditor.Text);
        }

        private void RenderMarkdown(string markdown)
        {
            var htmlParams = Markdig.Markdown.ToHtml(markdown, new MarkdownPipelineBuilder().UseAdvancedExtensions().Build());
            var html = $"<!DOCTYPE html><html><head><meta charset=\"UTF-8\"><style>body{{font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; padding: 10px;}}</style></head><body>{htmlParams}</body></html>";
            MarkdownViewer.NavigateToString(html);
        }
    }
}
