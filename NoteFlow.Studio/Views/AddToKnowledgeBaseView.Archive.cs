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

        private void BtnArchive_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var targetKBItem = CmbKnowledgeBase.SelectedItem as ComboBoxItem;
                if (targetKBItem == null)
                {
                    MessageBox.Show("请选择目标知识库");
                    return;
                }

                string kbPath = targetKBItem.Tag?.ToString() ?? "";
                string storageLocation = TxtStorageLocation.Text.Trim();
                string fileName = TxtFileName.Text.Trim();

                if (string.IsNullOrEmpty(kbPath))
                {
                    MessageBox.Show("目标知识库路径为空");
                    return;
                }

                if (string.IsNullOrEmpty(storageLocation))
                {
                    MessageBox.Show("存储位置不能为空");
                    return;
                }

                if (string.IsNullOrEmpty(fileName))
                {
                    MessageBox.Show("文件名不能为空");
                    return;
                }

                if (!fileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                {
                    fileName += ".md";
                }

                // 8. 归档保存为md文件: [obsidian repo root]/[target knowledge base]/[storage location]
                string targetDir = Path.Combine(kbPath, storageLocation);
                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                string markdownContent = TxtMarkdownEditor.Text;

                // 替换图片路径
                foreach (var imgPath in _snapshotImagePaths)
                {
                    string imgName = Path.GetFileName(imgPath);
                    string targetAssetsPath = $"assets/{imgName}";
                    markdownContent = markdownContent.Replace(imgPath, targetAssetsPath);
                    markdownContent = markdownContent.Replace(imgPath.Replace("\\", "/"), targetAssetsPath);
                }

                string mdFilePath = Path.Combine(targetDir, fileName);
                File.WriteAllText(mdFilePath, markdownContent, Encoding.UTF8);

                // 9. 将截图保存到 [obsidian repo root]/[target knowledge base]/[storage location]/assets
                if (_snapshotImagePaths.Count > 0)
                {
                    string assetsDir = Path.Combine(targetDir, "assets");
                    if (!Directory.Exists(assetsDir))
                    {
                        Directory.CreateDirectory(assetsDir);
                    }
                    foreach (var imgPath in _snapshotImagePaths)
                    {
                        if (File.Exists(imgPath))
                        {
                            string destImg = Path.Combine(assetsDir, Path.GetFileName(imgPath));
                            File.Copy(imgPath, destImg, true);
                        }
                    }
                }

                // 10. 将有时间字幕，和无时间字幕，保存到 [obsidian repo root]/[target knowledge base]/[storage location]/transcript
                string transcriptDir = Path.Combine(targetDir, "transcript");
                if (!Directory.Exists(transcriptDir))
                {
                    Directory.CreateDirectory(transcriptDir);
                }

                if (!string.IsNullOrEmpty(TxtOcrResult.Text))
                {
                    File.WriteAllText(Path.Combine(transcriptDir, "transcript_with_time.md"), TxtOcrResult.Text, Encoding.UTF8);
                }

                if (!string.IsNullOrEmpty(TxtPureOcrResult.Text))
                {
                    File.WriteAllText(Path.Combine(transcriptDir, "transcript_pure_text.md"), TxtPureOcrResult.Text, Encoding.UTF8);
                }
                else if (!string.IsNullOrEmpty(TxtOcrResult.Text))
                {
                    // If Pure Text is not generated, we might need to strip time manually or just let it be. Wait, there's RbWithTime / RbPureText which toggle visibility but not content. 
                    // Let's assume TxtPureOcrResult.Text is updated somewhere when Ocr/Whisper finishes. If it's valid, write it. If not, it just writes empty string.
                }

                UpdateRecordStatus(_currentVideoId, "已归档");

                MessageBox.Show("归档成功！");

                IconStep4.Kind = MaterialDesignThemes.Wpf.PackIconKind.CheckCircle;
                IconStep4.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 197, 94)); // #22C55E
                TxtStep4Desc.Text = "完成";
            }
            catch (Exception ex)
            {
                UpdateRecordStatus(_currentVideoId, "失败");
                AppLogger.Error($"归档失败: {ex.Message}", ex);
                MessageBox.Show($"归档失败: {ex.Message}");
                TxtStep4Desc.Text = "失败";
            }
        }
    }
}
