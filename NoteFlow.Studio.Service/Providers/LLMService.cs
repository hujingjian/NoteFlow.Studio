using OpenAI;
using OpenAI.Chat;
using NoteFlow.Studio.Model.Config;
using NoteFlow.Studio.Repository.Config;
using NoteFlow.Studio.Service.Prompts;
using System.ClientModel;
using System.Text;
using NoteFlow.Studio.Infrastructure.Logging;

namespace NoteFlow.Studio.Service.Providers
{
    public sealed record VideoAnalysisFrame(double TimestampMs, byte[] JpegBytes);
    public sealed record VideoAnalysisImage(double TimestampMs, string ImageUrl);

    public class LLMService
    {
        private readonly AiModelRepository _aiModelRepository;

        public LLMService()
        {
            _aiModelRepository = new AiModelRepository();
        }

        // 获取客户端的私有辅助方法
        private ChatClient GetChatClient(AiModel model)
        {
            string endpoint = model.Endpoint.Trim().TrimEnd('/');

            // 1. 更加通用的版本号检测 (检测 /v1, /v2, /v4 等)
            // 智谱的 /v4 会被这个正则匹配到
            bool hasVersionPath = System.Text.RegularExpressions.Regex.IsMatch(endpoint, @"/v\d+");

            // 如果是官方 OpenAI 或者是像 Ollama 这种我们明确知道要补全的（如果没有版本号）
            bool isOllama = model.Name.ToLower().Contains("ollama");
            bool isDeepSeek = model.Name.ToLower().Contains("deepseek");

            // 逻辑：如果既没有版本号，也不是官方OpenAI域名，或者明确是Ollama但没写/v1
            if (!hasVersionPath && !isDeepSeek)
            {
                // 针对没有写版本号的本地部署或特定代理补全 /v1
                endpoint += "/v1";
            }

            // 2. 移除冗余的动作后缀
            // SDK 内部 CompleteChatStreamingAsync 会自动在后面加 /chat/completions
            string[] suffixesToRemove = { "/chat/completions", "/completions" };
            foreach (var suffix in suffixesToRemove)
            {
                if (endpoint.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    endpoint = endpoint.Substring(0, endpoint.Length - suffix.Length).TrimEnd('/');
                }
            }

            var options = new OpenAIClientOptions { Endpoint = new Uri(endpoint) };

            // 智谱 API 必须有 Key，Ollama 随意
            string apiKey = string.IsNullOrEmpty(model.ApiKey) ? "not-needed" : model.ApiKey;

            return new ChatClient(model.ModelName, new ApiKeyCredential(apiKey), options);
        }

        public List<AiModel> GetEnabledModels()
        {
            return _aiModelRepository.GetAll().Where(m => m.IsEnabled).ToList();
        }

        public async Task<List<string>> AiChunkingAsync(AiModel model, string text, string method, int maxWords)
        {
            if (model == null || string.IsNullOrEmpty(model.Endpoint)) return new List<string>();

            try
            {
                // 构建指令
                string instruction = $"你是一个专业的文本分块助手，请将用户提供的完整文案分块。所用的分块方式为【{method}】，如果您选用字数或段落切割，预期的最大字数上限约为 {maxWords} 字，如果是字数分割，要根据语义分析，不要将完整的句子截断。请输出分块结果，分块标识为<chunkchunk>，不要输出其他额外内容。";

                var messages = new List<ChatMessage>
        {
            new SystemChatMessage(instruction),
            new UserChatMessage($"请对以下文案进行分块：\n{text}")
        };

                // 普通调用 ChatCompletion
                var chatOptions = new ChatCompletionOptions
                {
                    MaxOutputTokenCount = 8000,
                    Temperature = 0.2f,
                    TopP = 0.8f,
                };

                AsyncCollectionResult<StreamingChatCompletionUpdate> updates = null;

                var client = GetChatClient(model);

                updates = client.CompleteChatStreamingAsync(messages);
                StringBuilder resultsb = new StringBuilder();

                // 4. 正常流式输出
                if (updates != null)
                {
                    IAsyncEnumerator<StreamingChatCompletionUpdate> enumerator = updates.GetAsyncEnumerator();
                    try
                    {
                        while (await enumerator.MoveNextAsync())
                        {
                            var update = enumerator.Current;
                            foreach (var part in update.ContentUpdate)
                            {
                                if (!string.IsNullOrEmpty(part.Text))
                                {
                                    resultsb.Append(part.Text);
                                }
                            }
                        }
                    }
                    finally
                    {
                        await enumerator.DisposeAsync();
                    }
                }
                // 按行解析分块
                var chunks = resultsb.ToString()
                    .Split(new[] { "<chunkchunk>" }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(c => c.Trim())
                    .Where(c => !string.IsNullOrEmpty(c))
                    .ToList();

                return chunks;
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Error during AI chunking: {ex.Message}", ex);
                return new List<string>();
            }
        }

        private class ChunksResponse
        {
            public List<string> Chunks { get; set; }
        }

        // 统一测试连接
        public async Task<bool> TestConnectionAsync(AiModel model)
        {
            if (model == null || string.IsNullOrEmpty(model.Endpoint)) return false;

            try
            {
                var client = GetChatClient(model);
                var messages = new List<ChatMessage> { new UserChatMessage("ping") };
                var chatOptions = new ChatCompletionOptions()
                {
                    MaxOutputTokenCount = 8000,
                    Temperature = 0.2f,
                    TopP = 0.8f
                };
                var result = await client.CompleteChatAsync(messages, chatOptions);
                return result.Value != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 使用按时间排序的视频关键帧拆解视觉效果，并生成不依赖原素材的 HTML 动效骨架。
        /// </summary>
        public async Task<List<VideoAnalysisImage>> UploadVideoAnalysisFramesAsync(
            IReadOnlyList<VideoAnalysisFrame> frames,
            IProgress<(int Current, int Total)>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (frames == null || frames.Count == 0)
                throw new ArgumentException("至少需要一帧画面。", nameof(frames));

            var uploader = new TencentCosImageUploader(TencentCosOptions.Load());
            var images = new List<VideoAnalysisImage>(frames.Count);
            for (var i = 0; i < frames.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report((i + 1, frames.Count));
                var url = await uploader.UploadJpegAsync(frames[i].JpegBytes, cancellationToken);
                images.Add(new VideoAnalysisImage(frames[i].TimestampMs, url));
            }
            return images;
        }

        public async IAsyncEnumerable<string> AnalyzeVideoRecreationStreamAsync(
            AiModel model,
            IReadOnlyList<VideoAnalysisImage> frames,
            double startMs,
            double endMs,
            string aspectRatio,
            string extraRequirements)
        {
            if (model == null || string.IsNullOrWhiteSpace(model.Endpoint))
                throw new InvalidOperationException("尚未配置可用的大模型。");
            if (frames == null || frames.Count == 0)
                throw new ArgumentException("至少需要一帧画面。", nameof(frames));

            var durationSeconds = Math.Max(0, endMs - startMs) / 1000d;
            var prompt = $"""
                你是一名高级动效导演、UI 视觉设计师和前端动画工程师。下面的图片是同一段视频按时间先后均匀抽取的关键帧，
                片段范围为 {FormatTimestamp(startMs)} 至 {FormatTimestamp(endMs)}，总时长约 {durationSeconds:0.###} 秒。

                请先完整拆解视觉表达：画布与构图、颜色、字体特征与排版、图形元素、层级、材质、阴影/模糊/发光、
                每个元素出现和离开的方式、运动路径、缩放旋转、遮罩、转场、缓动曲线、节拍与精确时间轴。关键帧无法直接证明的运动细节，
                请基于相邻帧做合理推断，并明确标记为“推断”。

                然后生成一个可直接保存并在浏览器打开的单文件 HTML，实现等时长、可自动播放和循环的复刻动效。要求：
                1. 仅使用 HTML、CSS、原生 JavaScript 和内联 SVG，不依赖 CDN、构建工具或外部库；
                2. 输出比例为“{aspectRatio}”；画面需响应式缩放，同时保持设计比例；
                3. 不复刻原始照片、人物或视频素材。需要素材的位置使用带虚线边框、名称和建议尺寸的清晰占位块，并集中定义方便替换；
                4. 字体优先用系统字体栈；如果无法确定字体，给出最接近的字体分类与替换建议；
                5. 动画必须自动播放、循环，并严格用 CSS keyframes 或 Web Animations API 表达完整时间轴；
                6. 加入“重新播放”和“暂停/继续”控件，但控件不要覆盖主体设计；
                7. 代码中用中文注释标出素材替换点、核心动效和时间节点；
                8. 不要省略代码，不要用伪代码，不要输出 Markdown 代码围栏。

                用户补充要求：{(string.IsNullOrWhiteSpace(extraRequirements) ? "无" : extraRequirements.Trim())}

                严格按以下结构返回：
                <ANALYSIS>
                用清晰的小标题给出完整视觉拆解和逐秒时间轴。
                </ANALYSIS>
                <HTML>
                从 <!doctype html> 开始的完整 HTML。
                </HTML>
                """;

            var parts = new List<ChatMessageContentPart>
            {
                ChatMessageContentPart.CreateTextPart(prompt)
            };
            foreach (var frame in frames)
            {
                parts.Add(ChatMessageContentPart.CreateTextPart($"关键帧 · {FormatTimestamp(frame.TimestampMs)}"));
                parts.Add(ChatMessageContentPart.CreateImagePart(
                    new Uri(frame.ImageUrl),
                    ChatImageDetailLevel.High));
            }

            var client = GetChatClient(model);
            var messages = new List<ChatMessage> { new UserChatMessage(parts) };
            var options = new ChatCompletionOptions
            {
                MaxOutputTokenCount = 16000,
                Temperature = 0.35f,
                TopP = 0.9f
            };

            var updates = client.CompleteChatStreamingAsync(messages, options);
            var hasContent = false;
            var enumerator = updates.GetAsyncEnumerator();
            try
            {
                while (await enumerator.MoveNextAsync())
                {
                    foreach (var part in enumerator.Current.ContentUpdate)
                    {
                        if (string.IsNullOrEmpty(part.Text)) continue;
                        hasContent = true;
                        yield return part.Text;
                    }
                }
            }
            finally
            {
                await enumerator.DisposeAsync();
            }
            if (!hasContent) throw new InvalidOperationException("模型没有返回可用内容，请确认所选模型支持图片输入。");
        }

        private static string FormatTimestamp(double milliseconds)
        {
            var time = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
            return time.ToString(@"mm\:ss\.fff");
        }

        /// <summary>
        /// 统一流式输出
        /// </summary>
        public async IAsyncEnumerable<string> GenerateMarkdownStreamAsync(AiModel model, string text, string videoUrl)
        {
            // 1. 预校验
            if (model == null || string.IsNullOrEmpty(model.Endpoint))
            {
                yield return "Error: Model or API URL not configured.";
                yield break;
            }

            // 2. 准备变量以规避 CS1631
            string errorMessage = null;
            AsyncCollectionResult<StreamingChatCompletionUpdate> updates = null;

            try
            {
                var promptStr = GenerateMarkdownPromptStrategy.prompt
                                .Replace("{{video_url}}", videoUrl)
                                .Replace("{{subtitle_content}}", text);

                var client = GetChatClient(model);
                var messages = new List<ChatMessage> { new UserChatMessage(promptStr) };

                updates = client.CompleteChatStreamingAsync(messages);
            }
            catch (Exception ex)
            {
                // 只是记录错误，不在 catch 里 yield
                errorMessage = $"Error initializing stream: {ex.Message}";
                AppLogger.Error(errorMessage, ex);
            }

            // 3. 在 try-catch 外部 yield 错误
            if (errorMessage != null)
            {
                yield return errorMessage;
                yield break;
            }

            // 4. 正常流式输出
            if (updates != null)
            {
                IAsyncEnumerator<StreamingChatCompletionUpdate> enumerator = updates.GetAsyncEnumerator();
                try
                {
                    while (await enumerator.MoveNextAsync())
                    {
                        var update = enumerator.Current;
                        foreach (var part in update.ContentUpdate)
                        {
                            if (!string.IsNullOrEmpty(part.Text))
                            {
                                yield return part.Text;
                            }
                        }
                    }
                }
                finally
                {
                    await enumerator.DisposeAsync();
                }
            }
        }
    }
}
