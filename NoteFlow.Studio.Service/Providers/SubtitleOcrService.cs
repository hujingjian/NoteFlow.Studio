using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;
using PaddleOCRSharp;
using NoteFlow.Studio.Infrastructure.Logging;

namespace NoteFlow.Studio.Service.Providers
{
    public class SubtitleItem
    {
        public int Index { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public string Text { get; set; }

        public override string ToString()
        {
            //return $"{Index}\r\n{StartTime:hh\\:mm\\:ss\\,fff} --> {EndTime:hh\\:mm\\:ss\\,fff}\r\n{Text}\r\n";
            return $"{StartTime:hh\\:mm\\:ss\\,fff} --> {EndTime:hh\\:mm\\:ss\\,fff} {Text}\r\n";
        }
    }

    public class SubtitleOcrService
    {
        private readonly string _ffmpegPath;

        private double CalculateSimilarity(string source, string target)
        {
            if (string.IsNullOrEmpty(source) && string.IsNullOrEmpty(target)) return 1.0;
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target)) return 0.0;
            if (source == target) return 1.0;
            
            int n = source.Length;
            int m = target.Length;
            int[,] d = new int[n + 1, m + 1];

            for (int i = 0; i <= n; d[i, 0] = i++) { }
            for (int j = 0; j <= m; d[0, j] = j++) { }

            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    int cost = (target[j - 1] == source[i - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }
            
            int maxLen = Math.Max(n, m);
            return 1.0 - ((double)d[n, m] / maxLen);
        }

        public SubtitleOcrService(string ffmpegPath)
        {
            _ffmpegPath = ffmpegPath;
            if (!Directory.Exists(_ffmpegPath))
            {
                Directory.CreateDirectory(_ffmpegPath);
            }
            FFmpeg.SetExecutablesPath(_ffmpegPath);
        }

        private void MapVisualToVideoCoordinates(
            double cropX, double cropY, double cropW, double cropH,
            double visualW, double visualH, double videoW, double videoH,
            out int realX, out int realY, out int realW, out int realH)
        {
            double videoRatio = videoW / videoH;
            double containerRatio = visualW / visualH;

            double actualDrawWidth, actualDrawHeight;
            double offsetX = 0, offsetY = 0;

            if (videoRatio > containerRatio)
            {
                actualDrawWidth = visualW;
                actualDrawHeight = visualW / videoRatio;
                offsetY = (visualH - actualDrawHeight) / 2.0;
            }
            else
            {
                actualDrawHeight = visualH;
                actualDrawWidth = visualH * videoRatio;
                offsetX = (visualW - actualDrawWidth) / 2.0;
            }

            double scaleX = videoW / actualDrawWidth;
            double scaleY = videoH / actualDrawHeight;

            realX = (int)((cropX - offsetX) * scaleX);
            realY = (int)((cropY - offsetY) * scaleY);
            realW = (int)(cropW * scaleX);
            realH = (int)(cropH * scaleY);

            // Clamp
            if (realX < 0) { realW += realX; realX = 0; }
            if (realY < 0) { realH += realY; realY = 0; }
            if (realX + realW > videoW) realW = (int)videoW - realX;
            if (realY + realH > videoH) realH = (int)videoH - realY;
        }

        public async Task<string> ExtractImageTextAsync(string imagePath, double cropX, double cropY, double cropW, double cropH, double visualW, double visualH)
        {
            return await Task.Run(() => 
            {
                // To keep it simple, we could crop via Engine / Bitmap or pass the Full image. 
                // The prompt says "宽度自动改为和视频宽度一致", so it's the whole width.
                // We'll crop the image using System.Drawing
                using var bmp = new Bitmap(imagePath);
                
                MapVisualToVideoCoordinates(cropX, cropY, cropW, cropH, visualW, visualH, bmp.Width, bmp.Height, out int realX, out int realY, out int realW, out int realH);

                if (realW <= 0 || realH <= 0) 
                {
                    realX = 0; realY = 0; realW = bmp.Width; realH = bmp.Height;
                }

                var cropRect = new Rectangle(realX, realY, realW, realH);
                using var croppedBmp = bmp.Clone(cropRect, bmp.PixelFormat);
                
                // Save temp
                string tempFile = Path.GetTempFileName() + ".jpg";
                croppedBmp.Save(tempFile, System.Drawing.Imaging.ImageFormat.Jpeg);

                try 
                {
                    OCRModelConfig config = null;
                    OCRParameter oCRParameter = new OCRParameter();
                    PaddleOCREngine engine = new PaddleOCREngine(config, oCRParameter);
                    try
                    {
                        var res = engine.DetectText(tempFile);
                        var texts = res.TextBlocks.Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t));
                        return string.Join(" ", texts).Trim();
                    }
                    finally
                    {
                        engine.Dispose();
                    }
                }
                finally
                {
                    if (File.Exists(tempFile)) File.Delete(tempFile);
                }
            });
        }

        /// <summary>
        /// Extract subtitles from video using OCR mapped from visual coordinate ratio
        /// cropRect is the visual crop bounds, visualVideoWidth/visualVideoHeight are the visual container size
        /// </summary>
        public async Task<string> ExtractSubtitlesAsync(
            string videoFilePath, 
            double cropX, double cropY, double cropW, double cropH, 
            double visualVideoWidth, double visualVideoHeight,
            IProgress<int> progress,
            CancellationToken cancellationToken,
            Action<SubtitleItem> onSubtitleExtracted = null)
        {
            try
            {
                var mediaInfo = await FFmpeg.GetMediaInfo(videoFilePath);
                var videoStream = mediaInfo.VideoStreams.FirstOrDefault();
                if (videoStream == null) throw new Exception("No video stream found.");

                // Map coordinates
                MapVisualToVideoCoordinates(cropX, cropY, cropW, cropH, visualVideoWidth, visualVideoHeight, videoStream.Width, videoStream.Height, out int realX, out int realY, out int realW, out int realH);
                
                // Ensure boundaries
                if (realW <= 0 || realH <= 0 || realX >= videoStream.Width || realY >= videoStream.Height)
                {
                    realX = 0; realY = 0; realW = videoStream.Width; realH = videoStream.Height;
                }

                AppLogger.Info($"Video Resolution: {videoStream.Width}x{videoStream.Height}, Crop rect: mapped to X={realX}, Y={realY}, W={realW}, H={realH}");

                // Settings
                double fps = videoStream.Framerate;
                double durationSec = mediaInfo.Duration.TotalSeconds;
                
                // we extract 1 or 2 frames per second
                double extractFps = 1.0; 
                int totalFrames = (int)(durationSec * extractFps);

                var tempDir = Path.Combine(Path.GetTempPath(), "OCRTemp_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);
                
                var croppedVideoPath = Path.Combine(tempDir, "cropped.mp4");
                
                AppLogger.Info("Generating cropped temp video...");
                // Note: instead of extracting frame by frame which is slow, we can use ffmpeg to directly crop and output image sequences
                // Actually to avoid massive disk I/O, we can output at 1 FPS
                var outputPathPattern = Path.Combine(tempDir, "frame_%05d.jpg");
                var conversion = FFmpeg.Conversions.New()
                    .AddParameter($"-i \"{videoFilePath}\"")
                    .AddParameter($"-vf \"crop={realW}:{realH}:{realX}:{realY},fps={extractFps}\"")
                    .AddParameter("-q:v 2")
                    .SetOutput(outputPathPattern);

                await conversion.Start(cancellationToken);

                AppLogger.Info("Images extracted, starting PaddleOCR analysis...");
                
                var imageFiles = Directory.GetFiles(tempDir, "frame_*.jpg").OrderBy(f => f).ToList();
                int processed = 0;

                // Init PaddleOCR
                OCRModelConfig config = null;
                OCRParameter oCRParameter = new  OCRParameter();
                List<SubtitleItem> results = new List<SubtitleItem>();
                
                PaddleOCREngine engine = new PaddleOCREngine(config, oCRParameter);
                try
                {
                    string lastText = "";
                    TimeSpan? currentStartTime = null;
                    
                    for (int i = 0; i < imageFiles.Count; i++)
                    {
                        if (cancellationToken.IsCancellationRequested) break;

                        var res = engine.DetectText(imageFiles[i]);
                        var texts = res.TextBlocks.Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t));
                        string mergedText = string.Join(" ", texts).Trim();

                        TimeSpan ts = TimeSpan.FromSeconds(i * (1.0 / extractFps));

                        double similarity = CalculateSimilarity(mergedText, lastText);

                        if (!string.IsNullOrEmpty(mergedText))
                        {
                            if (similarity < 0.8)
                            {
                                // Different subtitle, export the old one
                                if (currentStartTime != null && !string.IsNullOrEmpty(lastText))
                                {
                                    var item = new SubtitleItem 
                                    { 
                                        Index = results.Count + 1,
                                        StartTime = currentStartTime.Value, 
                                        EndTime = ts, 
                                        Text = lastText 
                                    };
                                    results.Add(item);
                                    onSubtitleExtracted?.Invoke(item);
                                    AppLogger.Info($"Exported: {item}");
                                }
                                lastText = mergedText;
                                currentStartTime = ts;
                            }
                        }
                        else
                        {
                            // Empty text, export the previous one if exists
                            if (currentStartTime != null && !string.IsNullOrEmpty(lastText))
                            {
                                var item = new SubtitleItem 
                                { 
                                    Index = results.Count + 1,
                                    StartTime = currentStartTime.Value, 
                                    EndTime = ts, 
                                    Text = lastText 
                                };
                                results.Add(item);
                                onSubtitleExtracted?.Invoke(item);
                                AppLogger.Info($"Exported: {item}");
                            }
                            lastText = "";
                            currentStartTime = null;
                        }

                        processed++;
                        progress?.Report((int)((processed / (double)imageFiles.Count) * 100));
                    }

                    // Export the final one if remaining
                    if (currentStartTime != null && !string.IsNullOrEmpty(lastText))
                    {
                        TimeSpan endTs = TimeSpan.FromSeconds(imageFiles.Count * (1.0 / extractFps));
                        var item = new SubtitleItem 
                        { 
                            Index = results.Count + 1,
                            StartTime = currentStartTime.Value, 
                            EndTime = endTs, 
                            Text = lastText 
                        };
                        results.Add(item);
                        onSubtitleExtracted?.Invoke(item);
                        AppLogger.Info($"Exported: {item}");
                    }
                }
                finally
                {
                    engine.Dispose();
                }

                // Cleanup
                try { Directory.Delete(tempDir, true); } catch (Exception ex) { AppLogger.Error("An error occurred while deleting temp directory.", ex); }

                var finalLines = results.Select(r => r.ToString());
                return string.Join(Environment.NewLine, finalLines);
            }
            catch (Exception ex)
            {
                AppLogger.Error("OCR Extraction failed", ex);
                throw;
            }
        }
    }
}