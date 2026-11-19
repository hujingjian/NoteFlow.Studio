using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NoteFlow.Studio.Service.Providers
{
    internal sealed class TencentCosOptions
    {
        public string SecretId { get; init; } = string.Empty;
        public string SecretKey { get; init; } = string.Empty;
        public string Region { get; init; } = string.Empty;
        public string DefaultBucket { get; init; } = string.Empty;
        public string Domain { get; init; } = string.Empty;
        public int DaysToKeep { get; init; } = 3;

        public static TencentCosOptions Load()
        {
            var fileOptions = LoadFromFile();
            var result = new TencentCosOptions
            {
                SecretId = Environment.GetEnvironmentVariable("NOTEFLOW_COS_SECRET_ID") ?? fileOptions?.SecretId ?? string.Empty,
                SecretKey = Environment.GetEnvironmentVariable("NOTEFLOW_COS_SECRET_KEY") ?? fileOptions?.SecretKey ?? string.Empty,
                Region = Environment.GetEnvironmentVariable("NOTEFLOW_COS_REGION") ?? fileOptions?.Region ?? string.Empty,
                DefaultBucket = Environment.GetEnvironmentVariable("NOTEFLOW_COS_BUCKET") ?? fileOptions?.DefaultBucket ?? string.Empty,
                Domain = Environment.GetEnvironmentVariable("NOTEFLOW_COS_DOMAIN") ?? fileOptions?.Domain ?? string.Empty,
                DaysToKeep = int.TryParse(Environment.GetEnvironmentVariable("NOTEFLOW_COS_DAYS_TO_KEEP"), out var days)
                    ? days
                    : fileOptions?.DaysToKeep ?? 3
            };

            if (string.IsNullOrWhiteSpace(result.SecretId) || string.IsNullOrWhiteSpace(result.SecretKey) ||
                string.IsNullOrWhiteSpace(result.Region) || string.IsNullOrWhiteSpace(result.DefaultBucket))
            {
                throw new InvalidOperationException("腾讯 COS 配置不完整，请检查 appsettings.local.json。 ");
            }
            return result;
        }

        private static TencentCosOptions? LoadFromFile()
        {
            var paths = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.local.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json")
            };
            foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!File.Exists(path)) continue;
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                if (!document.RootElement.TryGetProperty("TencentCOS", out var section)) continue;
                return JsonSerializer.Deserialize<TencentCosOptions>(section.GetRawText(), new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            return null;
        }
    }

    internal sealed class TencentCosImageUploader
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };
        private readonly TencentCosOptions _options;
        private readonly Uri _origin;

        public TencentCosImageUploader(TencentCosOptions options)
        {
            _options = options;
            var defaultDomain = $"https://{options.DefaultBucket}.cos.{options.Region}.myqcloud.com";
            _origin = new Uri((string.IsNullOrWhiteSpace(options.Domain) ? defaultDomain : options.Domain).TrimEnd('/') + "/");
        }

        public async Task<string> UploadJpegAsync(byte[] bytes, CancellationToken cancellationToken = default)
        {
            var objectKey = $"noteflow/video-recreation/{DateTime.UtcNow:yyyyMMdd}/{Guid.NewGuid():N}.jpg";
            var encodedPath = string.Join('/', objectKey.Split('/').Select(Uri.EscapeDataString));
            var target = new Uri(_origin, encodedPath);
            var authorization = CreateAuthorization("put", target.AbsolutePath, target.Host, 600);

            using var request = new HttpRequestMessage(HttpMethod.Put, target);
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
            request.Content = new ByteArrayContent(bytes);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            request.Headers.CacheControl = new CacheControlHeaderValue
            {
                Public = true,
                MaxAge = TimeSpan.FromDays(Math.Max(1, _options.DaysToKeep))
            };

            using var response = await Http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException($"COS 上传失败（{(int)response.StatusCode}）：{body}");
            }
            var readAuthorization = CreateAuthorization(
                "get",
                target.AbsolutePath,
                target.Host,
                Math.Clamp(_options.DaysToKeep, 1, 7) * 24 * 60 * 60);
            var signedUrl = new UriBuilder(target) { Query = readAuthorization };
            return signedUrl.Uri.ToString();
        }

        private string CreateAuthorization(string method, string uriPath, string host, int validForSeconds)
        {
            var start = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var end = start + Math.Max(60, validForSeconds);
            var keyTime = $"{start};{end}";
            var canonicalHeaders = $"host={Uri.EscapeDataString(host.ToLowerInvariant())}";
            var httpString = $"{method.ToLowerInvariant()}\n{uriPath}\n\n{canonicalHeaders}\n";
            var stringToSign = $"sha1\n{keyTime}\n{Sha1Hex(httpString)}\n";
            var signKey = HmacSha1Hex(Encoding.UTF8.GetBytes(_options.SecretKey), keyTime);
            var signature = HmacSha1Hex(Encoding.UTF8.GetBytes(signKey), stringToSign);
            return $"q-sign-algorithm=sha1&q-ak={Uri.EscapeDataString(_options.SecretId)}&q-sign-time={keyTime}&q-key-time={keyTime}&q-header-list=host&q-url-param-list=&q-signature={signature}";
        }

        private static string Sha1Hex(string value) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

        private static string HmacSha1Hex(byte[] key, string value)
        {
            using var hmac = new HMACSHA1(key);
            return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
        }
    }
}
