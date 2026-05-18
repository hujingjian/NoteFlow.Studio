using Microsoft.Playwright;
using System.Threading;
using System.Threading.Tasks;

namespace NoteFlow.Studio.Service.Providers
{
    public class PlaywrightProvider : IPlaywrightProvider
    {
        private static IPlaywright? _playwright;
        private static IBrowser? _browser;
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
    }
}