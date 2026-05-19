using NoteFlow.Studio.Infrastructure.Logging;
using NoteFlow.Studio.Service.Interfaces;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xabe.FFmpeg;

namespace NoteFlow.Studio.Service.Providers
{
    public class WhisperService : IWhisperService
    {
        public Task ValidateModelAsync(string modelPath)
        {
            if (!File.Exists(modelPath))
            {
                throw new FileNotFoundException($"找不到 Whisper 模型文件: {modelPath}");
            }
            return Task.CompletedTask;
        }

        public async Task<string> ExtractAudioToWavAsync(string videoFilePath, string toolDownloadDir, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(videoFilePath))
                throw new FileNotFoundException($"视频文件不存在: {videoFilePath}");

            var outputWav = Path.ChangeExtension(videoFilePath, ".wav");
            
            // Set FFmpeg executable path
            var ffmpegBinDir = Path.Combine(toolDownloadDir, "ffmpeg", "bin");
            if (Directory.Exists(ffmpegBinDir))
            {
                FFmpeg.SetExecutablesPath(ffmpegBinDir);
            }
            else
            {
                var ffmpegRoot = Path.Combine(toolDownloadDir, "ffmpeg");
                if (Directory.Exists(ffmpegRoot))
                {
                    FFmpeg.SetExecutablesPath(ffmpegRoot);
                }
            }

            var mediaInfo = await FFmpeg.GetMediaInfo(videoFilePath);
            var audioStream = mediaInfo.AudioStreams.GetEnumerator();
            
            if (!audioStream.MoveNext())
            {
                throw new Exception("视频中未找到音频流");
            }

            // Generate WAV: 16khz (Whisper default sample rate)
            var conversion = FFmpeg.Conversions.New()
                                   .AddStream(audioStream.Current)
                                   .SetOutput(outputWav)
                                   .AddParameter("-ar 16000 -ac 1");
            
            await conversion.Start(cancellationToken);

            return outputWav;
        }

        public async Task TranscribeAudioAsync(string wavFilePath, string modelPath, string language, Action<string, string> onSegmentRecognized, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(wavFilePath))
                throw new FileNotFoundException($"WAV文件不存在: {wavFilePath}");

            var whisperExePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "NoteFlow.Studio.Whisper.exe");
            if (!File.Exists(whisperExePath))
            {
                throw new FileNotFoundException($"Whisper可执行文件未找到: {whisperExePath}");
            }

            var processStartInfo = new ProcessStartInfo
            {
                FileName = whisperExePath,
                Arguments = $"\"{modelPath}\" \"{wavFilePath}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            using var process = new Process { StartInfo = processStartInfo };

            using var cancellationTokenRegistration = cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(true);
                    }
                }
                catch (Exception ex)
                {
                    // Log the exception
                    AppLogger.Error("Failed to kill Whisper process.", ex);
                }
            });

            process.Start();

            var reader = process.StandardOutput;
            while (!reader.EndOfStream)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = await reader.ReadLineAsync(cancellationToken);
                if (string.IsNullOrEmpty(line)) continue;

                try
                {
                    var segmentData = JsonSerializer.Deserialize<JsonElement>(line);
                    if (segmentData.TryGetProperty("Error", out var error))
                    {
                        throw new Exception($"Whisper process error: {error.GetString()}");
                    }

                    var startTime = TimeSpan.FromMilliseconds(segmentData.GetProperty("Start").GetInt64());
                    var endTime = TimeSpan.FromMilliseconds(segmentData.GetProperty("End").GetInt64());
                    var text = segmentData.GetProperty("Text").GetString();

                    string timeRange = $"[{FormatTimeSpan(startTime)} -> {FormatTimeSpan(endTime)}]";
                    string cleanText = text?.Trim();

                    if (!string.IsNullOrWhiteSpace(cleanText))
                    {
                        onSegmentRecognized?.Invoke(timeRange, cleanText);
                    }
                }
                catch (JsonException ex)
                {
                    AppLogger.Error($"Failed to parse segment JSON: {line}", ex);
                }
            }

            await process.WaitForExitAsync(cancellationToken);
        }

        private string FormatTimeSpan(TimeSpan ts)
        {
            return ts.ToString(@"hh\:mm\:ss\.fff");
        }
    }
}