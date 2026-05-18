using NoteFlow.Studio.Model.Config;
using NoteFlow.Studio.Repository.Config;
using NoteFlow.Studio.Service.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace NoteFlow.Studio.Service.Providers
{
    public class YtDlpService
    {
        private string GetToolDownloadPath()
        {
            var configRepo = new ConfigRepository();
            return configRepo.GetConfig(AppConfigKeys.ToolDownloadPath, "D:\\Downloads\\Tools");
        }

        public string GetYtDlpExecutable()
        {
            var envPaths = Environment.GetEnvironmentVariable("PATH");
            if (envPaths != null)
            {
                foreach (var path in envPaths.Split(Path.PathSeparator))
                {
                    try
                    {
                        var fullPath = Path.Combine(path.Trim('"'), "yt-dlp.exe");
                        if (File.Exists(fullPath)) return "yt-dlp";
                    }
                    catch { }
                }
            }

            var localPath = Path.Combine(GetToolDownloadPath(), "yt-dlp", "yt-dlp.exe");
            if (File.Exists(localPath)) return localPath;

            return null;
        }

        public async Task<bool> IsAvailableAsync()
        {
            var exe = GetYtDlpExecutable();
            if (exe == null) return false;

            try
            {
                using var process = new Process();
                process.StartInfo.FileName = exe == "yt-dlp" ? "yt-dlp" : exe;
                process.StartInfo.Arguments = "--version";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.CreateNoWindow = true;
                process.Start();
                await process.WaitForExitAsync();
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        public async Task InstallAsync(IProgress<double> progress)
        {
            var ytDir = Path.Combine(GetToolDownloadPath(), "yt-dlp");
            if (!Directory.Exists(ytDir)) Directory.CreateDirectory(ytDir);
            var ytPath = Path.Combine(ytDir, "yt-dlp.exe");

            using var http = new HttpClient();
            var url = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            var totalBytes = response.Content.Headers.ContentLength;

            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(ytPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
            var buffer = new byte[8192];
            var isMoreToRead = true;
            long totalRead = 0;

            do
            {
                var read = await contentStream.ReadAsync(buffer, 0, buffer.Length);
                if (read == 0)
                {
                    isMoreToRead = false;
                }
                else
                {
                    await fileStream.WriteAsync(buffer, 0, read);
                    totalRead += read;
                    if (totalBytes.HasValue && progress != null)
                    {
                        progress.Report((double)totalRead / totalBytes.Value * 100);
                    }
                }
            } while (isMoreToRead);
        }

        public async Task<string> UpdateAsync()
        {
            var exe = GetYtDlpExecutable();
            if (exe == null) throw new Exception("yt-dlp 未安装");

            using var process = new Process();
            process.StartInfo.FileName = exe == "yt-dlp" ? "yt-dlp" : exe;
            process.StartInfo.Arguments = "-U";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.CreateNoWindow = true;
            process.Start();

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            string fullOutput = output + error;
            if (fullOutput.Contains("up to date") || fullOutput.Contains("latest version"))
            {
                return "UP_TO_DATE";
            }
            if (process.ExitCode == 0)
            {
                return output;
            }
            throw new Exception(fullOutput);
        }

        public async Task<bool> HasSubtitlesAsync(string videoUrl, IEnumerable<CookieDto> cookies)
        {
            string cookieFilePath = null;
            var exe = GetYtDlpExecutable();
            if (exe == null) return false;

            try
            {
                cookieFilePath = await GenerateCookieFile(cookies);

                using var process = new Process();
                process.StartInfo.FileName = exe == "yt-dlp" ? "yt-dlp" : exe;
                process.StartInfo.Arguments = string.IsNullOrEmpty(cookieFilePath)
                    ? $"--list-subs \"{videoUrl}\""
                    : $"--cookies \"{cookieFilePath}\" --list-subs \"{videoUrl}\"";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.CreateNoWindow = true;
                process.Start();

                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                // 检查输出是否包含 "Available subtitles" 或 "Available automatic captions" 并且有实际类型的字幕
                return output.Contains("Available subtitles for") || output.Contains("Available automatic captions for");
            }
            catch
            {
                return false;
            }
            finally
            {
                if (File.Exists(cookieFilePath))
                {
                    try { File.Delete(cookieFilePath); } catch { }
                }
            }
        }

        public async Task<string> ExtractSubtitleAsync(string videoUrl, IEnumerable<CookieDto> cookies)
        {
            string cookieFilePath = null;

            var exe = GetYtDlpExecutable();
            if (exe == null) throw new Exception("yt-dlp 未安装");

            var configRepo = new ConfigRepository();
            string defaultDownloadDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Downloads");
            string downloadDir = configRepo.GetConfig(AppConfigKeys.VideoDownloadPath, defaultDownloadDir);
            if (string.IsNullOrWhiteSpace(downloadDir)) downloadDir = defaultDownloadDir;
            
            var tempDir = Path.Combine(downloadDir, "srt", Guid.NewGuid().ToString());
            if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);
            
            var templatePath = Path.Combine(tempDir);

            try
            {
                cookieFilePath = await GenerateCookieFile(cookies);
                var arguments = new StringBuilder();
                // cookies
                if (!string.IsNullOrWhiteSpace(cookieFilePath))
                {
                    arguments.Append($"--cookies \"{cookieFilePath}\" ");
                }
                // 强制使用 web client（避免 tv api challenge）
                arguments.Append("--extractor-args \"youtube:player_client=android\" ");
                // 优先人工字幕 + 自动字幕 fallback
                arguments.Append("--write-subs ");
                arguments.Append("--write-auto-subs ");
                // 中英字幕
                arguments.Append("--sub-langs \"zh.*,en.*\" ");
                // 转 srt
                arguments.Append("--convert-subs srt ");
                // 不下载视频
                arguments.Append("--skip-download ");
                // 非常重要：不选择视频 format
                arguments.Append("--force-write-subs ");
                // 输出目录
                arguments.Append($"-P \"{templatePath}\" ");
                // 文件名
                arguments.Append("-o \"%(id)s\" ");
                // URL
                arguments.Append($"\"{videoUrl}\"");

                using var process = new Process();
                process.StartInfo.FileName = exe == "yt-dlp" ? "yt-dlp" : exe;
                // 下载自动字幕或手动字幕
                process.StartInfo.Arguments = arguments.ToString();
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.CreateNoWindow = true;
                process.Start();

                await process.WaitForExitAsync();

                var files = Directory.GetFiles(tempDir, "*.*", SearchOption.TopDirectoryOnly);
                if (files.Length == 0) throw new Exception("未找到字幕文件");

                // 只读取第一个找到的字幕文件内容 (vtt 等格式)并做简单处理 (这里可以进一步优化VTT过滤)
                var fileContent = await File.ReadAllTextAsync(files[0]);

                // 简单的清理VTT格式以获取纯文本
                var lines = fileContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                var sb = new System.Text.StringBuilder();
                bool isHeader = true;
                foreach (var line in lines)
                {
                    if (isHeader)
                    {
                        if (line.Contains("-->")) isHeader = false;
                        continue;
                    }
                    if (line.Contains("-->") || string.IsNullOrWhiteSpace(line) || line.StartsWith("WEBVTT") || line.StartsWith("Kind:") || line.StartsWith("Language:")) continue;
                    // VTT timestamps can also have tags like <c> or 00:00:00.000. Here we do simple append.
                    // 去除由 yt-dlp 生成的 <c> 标签等
                    var cleanLine = System.Text.RegularExpressions.Regex.Replace(line, @"<[^>]+>", " ").Trim();
                    // 去除可能存在的时间戳
                    if (!System.Text.RegularExpressions.Regex.IsMatch(cleanLine, @"^\d{2}:\d{2}:\d{2}\.\d{3}"))
                    {
                        sb.AppendLine(cleanLine);
                    }
                }

                return sb.ToString().Trim();
            }
            finally
            {
                if (File.Exists(cookieFilePath))
                {
                    try { File.Delete(cookieFilePath); } catch { }
                }
                if (Directory.Exists(tempDir))
                {

                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        public async Task<string> ExtractVideoJsonAsync(string videoUrl, IEnumerable<CookieDto> cookies)
        {
            string cookieFilePath = null;

            try
            {
                cookieFilePath = await GenerateCookieFile(cookies);

                var exe = GetYtDlpExecutable();
                if (exe == null) throw new Exception("yt-dlp 未安装");

                var argumentsStr = "--js-runtimes node --dump-json --skip-download";
                if (!string.IsNullOrEmpty(cookieFilePath))
                {
                    argumentsStr += $" --cookies \"{cookieFilePath}\"";
                }
                argumentsStr += $" \"{videoUrl}\"";

                using var process = new Process();
                process.StartInfo.FileName = exe == "yt-dlp" ? "yt-dlp" : exe;
                process.StartInfo.Arguments = argumentsStr;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.CreateNoWindow = true;

                process.Start();
                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                return output;
            }
            catch(Exception ex)
            {
                throw new Exception("提取视频信息失败: " + ex.Message);
            }
            finally
            {
                if (File.Exists(cookieFilePath))
                {
                    try { File.Delete(cookieFilePath); } catch { }
                }
            }
            
        }

        public async Task<string> GenerateCookieFile(IEnumerable<CookieDto> cookies)
        {
            var cookieFilePath = Path.Combine(Path.GetTempPath(), $"yt_cookies_{Guid.NewGuid()}.txt");
            // Write cookies in Netscape format
            if (cookies != null)
            {
                using var writer = new StreamWriter(cookieFilePath);
                writer.WriteLine("# Netscape HTTP Cookie File");
                writer.WriteLine("# http://curl.haxx.se/rfc/cookie_spec.html");
                writer.WriteLine("# This is a generated file!  Do not edit.");
                writer.WriteLine("");
                foreach (var c in cookies)
                {
                    var domainStr = c.Domain?.StartsWith(".") == true ? "TRUE" : "FALSE";
                    var secureStr = c.Secure ? "TRUE" : "FALSE";
                    var expires = c.Secure ? "2147483647" : "0"; // Just a mock expiry if missing
                    writer.WriteLine($"{c.Domain}\t{domainStr}\t{c.Path}\t{secureStr}\t{expires}\t{c.Name}\t{c.Value}");
                }
            }
            return cookieFilePath;
        }

        public async Task<string> DownloadVideoAsync(string videoUrl, string downloadDir, IEnumerable<CookieDto> cookies)
        {
            string cookieFilePath = null;
            try
            {
                cookieFilePath = await GenerateCookieFile(cookies);

                var exe = GetYtDlpExecutable();
                if (exe == null) throw new Exception("yt-dlp 未安装");

                if (!Directory.Exists(downloadDir))
                {
                    Directory.CreateDirectory(downloadDir);
                }

                var outputTemplate = Path.Combine(downloadDir, "%(title)s.%(ext)s");

                var arguments = new System.Text.StringBuilder("-f bestvideo[ext=mp4]+bestaudio[ext=m4a]/best[ext=mp4]/best");
                if (!string.IsNullOrEmpty(cookieFilePath))
                {
                    arguments.Append($" --cookies \"{cookieFilePath}\"");
                }
                arguments.Append($" -o \"{outputTemplate}\"");
                arguments.Append($" \"{videoUrl}\"");

                using var process = new Process();
                process.StartInfo.FileName = exe == "yt-dlp" ? "yt-dlp" : exe;
                process.StartInfo.Arguments = arguments.ToString();
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.CreateNoWindow = true;

                process.Start();

                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();

                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    throw new Exception($"下载视频失败: {error}");
                }

                // 解析输出以找到文件名
                var match = System.Text.RegularExpressions.Regex.Match(output, @"\[download\] Destination: (.*?)\n");
                if (match.Success)
                {
                    return match.Groups[1].Value.Trim();
                }
                // 如果无法从输出中解析，则尝试在目录中查找最新创建的文件
                var directory = new DirectoryInfo(downloadDir);
                var latestFile = directory.GetFiles()
                                          .OrderByDescending(f => f.LastWriteTime)
                                          .FirstOrDefault();
                if (latestFile != null)
                {
                    return latestFile.FullName;
                }
                return string.Empty;
            }
            catch(Exception ex)
            {
                throw new Exception("下载完成，但无法确定下载的文件路径。");
            }
            finally
            {
                if (File.Exists(cookieFilePath))
                {
                    try { File.Delete(cookieFilePath); } catch { }
                }
            }
        }
    }
}