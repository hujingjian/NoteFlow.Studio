using NoteFlow.Studio.Infrastructure.Logging;
using System;
using System.Collections.Concurrent;
using System.Text;
using CefSharp;
using CefSharp.Handler;

namespace NoteFlow.Studio.CefSharpHandlers
{
    public class CustomResourceRequestHandler : ResourceRequestHandler
    {
        private JsonResponseFilter _filter;
        private Action<string, string> _onJsonIntercepted;
        private string _url;

        public CustomResourceRequestHandler(Action<string, string> onJsonIntercepted)
        {
            _onJsonIntercepted = onJsonIntercepted;
        }

        protected override CefReturnValue OnBeforeResourceLoad(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, IRequestCallback callback)
        {
            if(request.Url.Contains("bilibili"))
            {
                var MobileUA = "Mozilla/5.0 (iPhone; CPU iPhone OS 16_6 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/16.6 Mobile/15E148 Safari/604.1";
                // 补充移动端常用Header
                request.Headers["User-Agent"] = MobileUA;
                request.SetHeaderByName("user-agent", MobileUA, true);

                // 手机标识（关键）
                request.SetHeaderByName("referer", "https://m.bilibili.com/", true);
                request.SetHeaderByName("accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8", true);
                request.SetHeaderByName("accept-encoding", "gzip, deflate, br", true);
                request.SetHeaderByName("accept-language", "zh-CN,zh;q=0.9,en-US;q=0.8,en;q=0.7", true);
                request.SetHeaderByName("accept-encoding", "gzip, deflate, br", true);
                request.SetHeaderByName("sec-fetch-user", "?1", true);
                request.SetHeaderByName("sec-fetch-mode", "navigate", true);
                request.SetHeaderByName("sec-fetch-dest", "document", true);
                request.SetHeaderByName("upgrade-insecure-requests", "1", true);
                request.SetHeaderByName("sec-ch-ua-platform", "mobile", true);
                request.SetHeaderByName("sec-ch-ua-mobile", "?1", true);

                //request.Headers.Set("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
                //request.Headers.Set("Accept-Language", "zh-CN,zh;q=0.9,en-US;q=0.8,en;q=0.7");
                //request.Headers.Set("Sec-CH-UA-Mobile", "?1"); // 关键：标识移动端
                //request.Headers.Set("Sec-Fetch-User", "?1");
                //request.Headers.Set("Sec-Fetch-Site", "none");
                //request.Headers.Set("Sec-Fetch-Mode", "navigate");
                //request.Headers.Set("Sec-Fetch-Dest", "document");
                //request.Headers.Set("Upgrade-Insecure-Requests", "1");
            }
            return CefReturnValue.Continue;
        }

        protected override IResponseFilter? GetResourceResponseFilter(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, IResponse response)
        {
            _url = request.Url;
            if (_url.Contains("aweme/v1/web/aweme/detail/") || 
                _url.Contains("/x/web-interface/wbi/view") ||
                _url.Contains("/x/web-interface/view") ||
                _url.Contains("/x/player/wbi/playurl") ||
                _url.Contains("/x/player/playurl"))
            {
                _filter = new JsonResponseFilter();
                return _filter;
            }
            return null;
        }

        protected override void OnResourceLoadComplete(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, IResponse response, UrlRequestStatus status, long receivedContentLength)
        {
            if (_filter != null)
            {
                try
                {
                    var data = _filter.Data;
                    if (data != null && data.Length > 0)
                    {
                        var json = Encoding.UTF8.GetString(data);
                        _onJsonIntercepted?.Invoke(_url, json);
                    }
                }
                catch (Exception ex)
                {
                    // 记录日志
                }
            }
        }
    }

    public class CustomRequestHandler : RequestHandler
    {
        private Action<string, string> _onJsonIntercepted;

        public CustomRequestHandler(Action<string, string> onJsonIntercepted)
        {
            _onJsonIntercepted = onJsonIntercepted;
        }

        protected override IResourceRequestHandler? GetResourceRequestHandler(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, bool isNavigation, bool isDownload, string requestInitiator, ref bool disableDefaultHandling)
        {
            if (request.Url.Contains("aweme/v1/web/aweme/detail/") || 
                request.Url.Contains("/x/web-interface/wbi/view") ||
                request.Url.Contains("/x/web-interface/view") ||
                request.Url.Contains("/x/player/wbi/playurl") ||
                request.Url.Contains("/x/player/playurl"))
            {
                return new CustomResourceRequestHandler(_onJsonIntercepted);
            }
            return base.GetResourceRequestHandler(chromiumWebBrowser, browser, frame, request, isNavigation, isDownload, requestInitiator, ref disableDefaultHandling);
        }
    }
}
