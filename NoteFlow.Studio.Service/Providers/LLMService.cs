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

            // 逻辑：如果既没有版本号，也不是官方OpenAI域名，或者明确是Ollama但没写/v1
            if (!hasVersionPath)
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