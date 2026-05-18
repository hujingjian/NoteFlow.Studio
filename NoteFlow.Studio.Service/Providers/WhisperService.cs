using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NoteFlow.Studio.Service.Interfaces;
using Whisper.net;
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

            var options = new WhisperFactoryOptions 
            {
                UseGpu = true // Try to use GPU if available
            };

            using var whisperFactory = WhisperFactory.FromPath(modelPath, options);

            var builder = whisperFactory.CreateBuilder()
                .WithThreads(Environment.ProcessorCount);
            
            if (!string.IsNullOrEmpty(language) && language != "auto")
            {
                builder = builder.WithLanguage(language);
            }

            using var processor = builder.Build();

            using var fileStream = File.OpenRead(wavFilePath);
            
            await foreach (var segment in processor.ProcessAsync(fileStream, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                string timeRange = $"[{FormatTimeSpan(segment.Start)} -> {FormatTimeSpan(segment.End)}]";
                string cleanText = segment.Text.Trim();
                
                if (!string.IsNullOrWhiteSpace(cleanText))
                {
                    onSegmentRecognized?.Invoke(timeRange, cleanText);
                }
            }
        }

        private string FormatTimeSpan(TimeSpan ts)
        {
            return ts.ToString(@"hh\:mm\:ss\.fff");
        }
    }
}