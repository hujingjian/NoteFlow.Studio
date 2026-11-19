using CefSharp;
using CefSharp.Wpf;
using NoteFlow.Studio.Infrastructure.Logging;
using NoteFlow.Studio.Model.Config;
using NoteFlow.Studio.Repository.Config;
using System;
using System.Globalization;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace NoteFlow.Studio.Views
{
    public partial class FeishuArticleCaptureView : UserControl
    {
        private readonly ChromiumWebBrowser _browser;
        private bool _isExtracting;

        public FeishuArticleCaptureView()
        {
            InitializeComponent();
            _browser = new ChromiumWebBrowser { Address = "https://www.feishu.cn/", Background = System.Windows.Media.Brushes.Transparent };
            _browser.AddressChanged += Browser_AddressChanged;
            _browser.FrameLoadEnd += Browser_FrameLoadEnd;
            BrowserHost.Children.Add(_browser);
            Loaded += (s, e) => LoadKnowledgeBases();
        }

        private void Browser_FrameLoadEnd(object sender, FrameLoadEndEventArgs e)
        {
            if (!e.Frame.IsValid)
            {
                return;
            }

            e.Frame.ExecuteJavaScriptAsync(SelectionCacheScript);
        }

        private void Browser_AddressChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            Dispatcher.InvokeAsync(() => { if (e.NewValue is string url && !string.IsNullOrWhiteSpace(url)) TxtUrl.Text = url; });
        }

        private void BtnOpen_Click(object sender, RoutedEventArgs e) => Navigate();
        private void TxtUrl_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) Navigate(); }

        private void Navigate()
        {
            var url = TxtUrl.Text.Trim();
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                MessageBox.Show("请输入有效的 http 或 https 链接。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            _browser.Address = uri.ToString();
            TxtStatus.Text = "正在加载...";
        }

        private async void BtnExtract_Click(object sender, RoutedEventArgs e)
        {
            if (_isExtracting || !_browser.IsBrowserInitialized || _browser.IsDisposed) return;
            _isExtracting = true;
            BtnExtract.IsEnabled = false;
            TxtStatus.Text = "正在提取...";
            try
            {
                await Task.Delay(500);
                var titleResult = await _browser.EvaluateScriptAsync("document.title || document.querySelector('h1')?.innerText || ''");
                var title = titleResult.Success ? titleResult.Result?.ToString()?.Trim() : "";
                var body = await ReadPageTextAsync();
                if (string.IsNullOrWhiteSpace(body))
                {
                    TxtStatus.Text = "没有提取到可见正文";
                    MessageBox.Show("请确认已经登录，并等待文章加载完成。", "提取失败", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                title = string.IsNullOrWhiteSpace(title) ? "网页文章" : title;
                TxtFileName.Text = SanitizeFileName(title) + ".md";
                TxtStorageLocation.Text = SanitizePathPart(title);
                TxtMarkdown.Text = BuildMarkdown(title, _browser.Address, body);
                TxtStatus.Text = $"已提取 {body.Length:N0} 字";
            }
            catch (Exception ex)
            {
                AppLogger.Error($"提取网页内容失败: {ex.Message}", ex);
                TxtStatus.Text = "提取失败";
                MessageBox.Show($"提取失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { _isExtracting = false; BtnExtract.IsEnabled = true; }
        }

        private async void BtnCopySelection_Click(object sender, RoutedEventArgs e)
        {
            if (!_browser.IsBrowserInitialized || _browser.IsDisposed)
            {
                return;
            }

            try
            {
                TxtStatus.Text = "正在读取选中内容...";
                var selectedText = await ReadSelectedTextAsync();

                if (string.IsNullOrWhiteSpace(selectedText))
                {
                    TxtStatus.Text = "没有读取到选中内容";
                    MessageBox.Show("请先在网页中点击正文，然后按 Ctrl+A 全选，再点击“复制选中内容”。",
                        "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var titleResult = await _browser.EvaluateScriptAsync(
                    "document.title || document.querySelector('h1')?.innerText || ''");
                var title = titleResult.Success ? titleResult.Result?.ToString()?.Trim() : string.Empty;
                title = string.IsNullOrWhiteSpace(title) ? "网页文章" : title;

                TxtFileName.Text = SanitizeFileName(title) + ".md";
                TxtStorageLocation.Text = SanitizePathPart(title);
                TxtMarkdown.Text = BuildMarkdown(title, _browser.Address, selectedText);
                Clipboard.SetText(selectedText);
                TxtStatus.Text = $"已复制并读取 {selectedText.Length:N0} 字";
            }
            catch (Exception ex)
            {
                AppLogger.Error($"读取网页选中内容失败: {ex.Message}", ex);
                TxtStatus.Text = "读取失败";
                MessageBox.Show($"读取选中内容失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task<string> ReadSelectedTextAsync()
        {
            var browser = _browser.GetBrowser();
            if (browser == null || !browser.IsValid)
            {
                return string.Empty;
            }

            // The selection can disappear when the WPF button receives focus, so read the cached value first.
            foreach (var frame in browser.GetAllFrames())
            {
                if (!frame.IsValid) continue;
                var cached = await frame.EvaluateScriptAsync(
                    "window.__noteFlowSelection || ''");
                var cachedText = cached.Success ? cached.Result?.ToString()?.Trim() : string.Empty;
                if (!string.IsNullOrWhiteSpace(cachedText))
                {
                    return NormalizeCapturedText(cachedText);
                }
            }

            // Fallback 1: read visible text nodes directly from the rendered DOM.
            return await ReadPageTextAsync();
        }

        private async Task<string> ReadPageTextAsync()
        {
            var visibleText = await ReadVisibleTextAsync();
            if (IsSufficientCapture(visibleText))
            {
                return visibleText;
            }

            // Virtualized pages may render more content as the user scrolls.
            var scrolledText = await ReadByScrollingAsync();
            return IsSufficientCapture(scrolledText) ? scrolledText : visibleText;
        }

        private async Task<string> ReadVisibleTextAsync()
        {
            var browser = _browser.GetBrowser();
            if (browser == null || !browser.IsValid) return string.Empty;

            const string script = @"
                (() => {
                    const ignored = new Set(['SCRIPT','STYLE','NOSCRIPT','SVG','NAV','HEADER','FOOTER']);
                    const visible = element => {
                        if (!element || ignored.has(element.tagName)) return false;
                        const style = getComputedStyle(element);
                        return style.display !== 'none' &&
                               style.visibility !== 'hidden' &&
                               style.opacity !== '0';
                    };
                    const walker = document.createTreeWalker(document.body || document.documentElement, NodeFilter.SHOW_TEXT);
                    const lines = [];
                    let node;
                    while (node = walker.nextNode()) {
                        if (!node.parentElement || !visible(node.parentElement)) continue;
                        let parent = node.parentElement;
                        let valid = true;
                        while (parent && parent !== document.body) {
                            if (!visible(parent)) { valid = false; break; }
                            parent = parent.parentElement;
                        }
                        if (!valid) continue;
                        const text = (node.nodeValue || '').replace(/\u00a0/g, ' ').trim();
                        if (text) lines.push(text);
                    }
                    return lines.join('\n');
                })()";

            foreach (var frame in browser.GetAllFrames())
            {
                if (!frame.IsValid) continue;
                var result = await frame.EvaluateScriptAsync(script);
                var text = result.Success ? result.Result?.ToString() : string.Empty;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return NormalizeCapturedText(text);
                }
            }

            return string.Empty;
        }

        private async Task<string> ReadByScrollingAsync()
        {
            var mainFrame = _browser.GetBrowser()?.MainFrame;
            if (mainFrame == null || !mainFrame.IsValid) return string.Empty;

            const string captureScript = @"
                (() => {
                    const root = document.scrollingElement || document.documentElement;
                    const text = document.body ? document.body.innerText : '';
                    return JSON.stringify({
                        text: text || '',
                        top: root.scrollTop,
                        height: root.scrollHeight,
                        viewport: window.innerHeight || 800
                    });
                })()";
            var first = await mainFrame.EvaluateScriptAsync(captureScript);
            if (!TryParseScrollCapture(first.Result, out var firstText, out var originalTop, out _, out var viewport))
            {
                return string.Empty;
            }

            var parts = new List<string> { firstText };
            var step = Math.Max(300, (int)(viewport * 0.8));
            var maxIterations = 120;

            for (var i = 0; i < maxIterations; i++)
            {
                await mainFrame.EvaluateScriptAsync(
                    $"(() => {{ const root = document.scrollingElement || document.documentElement; root.scrollTop = Math.min(root.scrollTop + {step.ToString(CultureInfo.InvariantCulture)}, root.scrollHeight); }})()");
                await Task.Delay(180);
                var result = await mainFrame.EvaluateScriptAsync(captureScript);
                if (!TryParseScrollCapture(result.Result, out var text, out var top, out var height, out _))
                {
                    break;
                }

                parts.Add(text);
                if (top + viewport >= height - 4)
                {
                    break;
                }
            }

            await mainFrame.EvaluateScriptAsync(
                $"(() => {{ const root = document.scrollingElement || document.documentElement; root.scrollTop = {originalTop.ToString(CultureInfo.InvariantCulture)}; }})()");
            return NormalizeCapturedText(string.Join("\n", parts));
        }

        private static bool TryParseScrollCapture(object result, out string text, out double top, out double height, out double viewport)
        {
            text = string.Empty;
            top = height = viewport = 0;
            if (result == null) return false;

            try
            {
                using var document = JsonDocument.Parse(result.ToString() ?? string.Empty);
                var root = document.RootElement;
                text = root.TryGetProperty("text", out var textProperty) ? textProperty.GetString() ?? string.Empty : string.Empty;
                top = root.TryGetProperty("top", out var topProperty) ? topProperty.GetDouble() : 0;
                height = root.TryGetProperty("height", out var heightProperty) ? heightProperty.GetDouble() : 0;
                viewport = root.TryGetProperty("viewport", out var viewportProperty) ? viewportProperty.GetDouble() : 800;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string NormalizeCapturedText(string value)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var lines = value
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .Where(line => seen.Add(line));
            return string.Join("\n", lines).Trim();
        }

        private static bool IsSufficientCapture(string value) => !string.IsNullOrWhiteSpace(value) && value.Length >= 80;

        private const string SelectionCacheScript = @"
            (() => {
                if (window.__noteFlowSelectionInstalled) return;
                window.__noteFlowSelectionInstalled = true;
                window.__noteFlowSelection = window.__noteFlowSelection || '';
                const normalize = value => (value || '').replace(/\u00a0/g, ' ').trim();
                const save = () => {
                    const selection = window.getSelection ? normalize(window.getSelection().toString()) : '';
                    if (selection) window.__noteFlowSelection = selection;
                };
                document.addEventListener('selectionchange', save, true);
                document.addEventListener('keyup', event => {
                    if (event.ctrlKey || event.metaKey || event.key === 'a') setTimeout(save, 0);
                }, true);
            })();";

        private static string BuildMarkdown(string title, string url, string body)
        {
            var escapedTitle = title.Replace("\"", "\\\"");
            var escapedUrl = url.Replace("\"", "\\\"");
            return $"---\ntitle: \"{escapedTitle}\"\nsource: \"{escapedUrl}\"\ncaptured: \"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\"\n---\n\n# {title}\n\n> 来源：[{url}]({url})\n\n{body.Replace("\r\n", "\n").Replace("\r", "\n")}\n";
        }

        private void LoadKnowledgeBases()
        {
            CmbKnowledgeBase.Items.Clear();
            var root = new ConfigRepository().GetConfig(AppConfigKeys.ObsidianRootPath, "");
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) { TxtStatus.Text = "请先配置 Obsidian 仓库"; return; }
            CmbKnowledgeBase.Items.Add(new ComboBoxItem { Content = "仓库根目录", Tag = root });
            foreach (var dir in Directory.GetDirectories(root))
            {
                var name = Path.GetFileName(dir);
                if (!name.StartsWith(".")) CmbKnowledgeBase.Items.Add(new ComboBoxItem { Content = name, Tag = dir });
            }
            CmbKnowledgeBase.SelectedIndex = 0;
        }

        private void TxtMarkdown_TextChanged(object sender, TextChangedEventArgs e) { BtnArchive.IsEnabled = !string.IsNullOrWhiteSpace(TxtMarkdown?.Text); }

        private void BtnArchive_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (CmbKnowledgeBase.SelectedItem is not ComboBoxItem item || item.Tag is not string root)
                { MessageBox.Show("请选择目标知识库。", "提示", MessageBoxButton.OK, MessageBoxImage.Information); return; }
                var folder = SanitizePathPart(TxtStorageLocation.Text.Trim());
                var fileName = SanitizeFileName(TxtFileName.Text.Trim());
                if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(fileName))
                { MessageBox.Show("请填写存储文件夹和文件名。", "提示", MessageBoxButton.OK, MessageBoxImage.Information); return; }
                if (!fileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) fileName += ".md";
                var directory = Path.Combine(root, folder);
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, fileName);
                File.WriteAllText(path, TxtMarkdown.Text, new UTF8Encoding(false));
                TxtStatus.Text = "归档成功";
                MessageBox.Show($"已归档到：{path}", "归档成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"归档网页内容失败: {ex.Message}", ex);
                TxtStatus.Text = "归档失败";
                MessageBox.Show($"归档失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string SanitizeFileName(string value)
        {
            var result = string.IsNullOrWhiteSpace(value) ? "网页文章" : value.Trim();
            foreach (var c in Path.GetInvalidFileNameChars()) result = result.Replace(c.ToString(), "");
            return result.Trim().Trim('.');
        }
        private static string SanitizePathPart(string value) => SanitizeFileName(value).Replace("/", "").Replace("\\", "");

        public void DisposeBrowser()
        {
            if (_browser.IsDisposed) return;
            try { _browser.Address = "about:blank"; _browser.Dispose(); }
            catch (Exception ex) { AppLogger.Warn($"释放网页浏览器失败: {ex.Message}"); }
        }
    }
}
