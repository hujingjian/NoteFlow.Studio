using CefSharp;
using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NoteFlow.Studio.CefSharpHandlers
{
    public class CookieVisitor : ICookieVisitor
    {
        private readonly TaskCompletionSource<List<Microsoft.Playwright.Cookie>> _tcs = new TaskCompletionSource<List<Microsoft.Playwright.Cookie>>();
        private readonly List<Microsoft.Playwright.Cookie> _cookies = new List<Microsoft.Playwright.Cookie>();

        public Task<List<Microsoft.Playwright.Cookie>> Task => _tcs.Task;

        public bool Visit(CefSharp.Cookie cookie, int count, int total, ref bool deleteCookie)
        {
            _cookies.Add(new Microsoft.Playwright.Cookie
            {
                Name = cookie.Name,
                Value = cookie.Value,
                Domain = cookie.Domain,
                Path = cookie.Path,
                Expires = cookie.Expires.HasValue ? new DateTimeOffset(cookie.Expires.Value).ToUnixTimeSeconds() : -1,
                Secure = cookie.Secure,
                HttpOnly = cookie.HttpOnly
            });

            if (count == total - 1)
            {
                _tcs.TrySetResult(_cookies);
            }

            return true;
        }

        public void Dispose()
        {
            // Do nothing
        }
    }
}
