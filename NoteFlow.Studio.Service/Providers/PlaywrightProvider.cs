using Microsoft.Playwright;
using System.Threading;
using System.Threading.Tasks;

namespace NoteFlow.Studio.Service.Providers
{
    public class PlaywrightProvider : IPlaywrightProvider
    {
        private static IPlaywright? _playwright;
        private static IBrowser? _browser;
        private static IBrowser? _headedBrowser;
        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        public async Task<IBrowser> GetBrowserAsync()
        {
            if (_browser != null)
            {
                return _browser;
            }

            await _semaphore.WaitAsync();
            try
            {
                if (_browser != null)
                {
                    return _browser;
                }

                _playwright ??= await Playwright.CreateAsync();
                
                _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = true,
                    SlowMo = 100, // 模拟人类操作速度，防机器检测
                    Args = new[]
                    {
                        "--no-sandbox",
                        "--disable-setuid-sandbox",
                        "--disable-dev-shm-usage",
                        "--disable-gpu",
                        "--no-first-run",
                        "--no-default-browser-check",
                        "--disable-default-apps",
                        "--disable-blink-features=AutomationControlled",
                        "--disable-web-security",
                        "--disable-features=VizDisplayCompositor"
                    }
                });

                return _browser;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task<IBrowser> GetHeadedBrowserAsync()
        {
            await _semaphore.WaitAsync();
            try
            {
                // 每次都创建新的有头浏览器实例，用完后调用方负责关闭
                _playwright ??= await Playwright.CreateAsync();
                var headedBrowser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = false,
                    SlowMo = 100,
                    Args = new[]
                    {
                        "--no-sandbox",
                        "--disable-setuid-sandbox",
                        "--disable-dev-shm-usage",
                        "--no-first-run",
                        "--no-default-browser-check",
                        "--disable-default-apps",
                        "--disable-blink-features=AutomationControlled"
                    }
                });
                return headedBrowser;
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }
}