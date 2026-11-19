using System.Configuration;
using System.Data;
using System.Windows;
using CefSharp;
using CefSharp.Wpf;

namespace NoteFlow.Studio
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            var settings = new CefSettings();
            
            // 隐藏 webdriver 标志，减少被检测的风险
            settings.CefCommandLineArgs.Add("enable-automation", "0");
            settings.CefCommandLineArgs.Add("disable-blink-features", "AutomationControlled");
            
            // 伪装 UserAgent (建议使用比较新的真实Chrome版本)
            settings.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36";
            
            // 隐藏 WebGL 和常见特征
            settings.CefCommandLineArgs.Add("disable-extensions", "1");
            settings.CefCommandLineArgs.Add("disable-web-security", "0");
            
            // 移除默认的 Cef 注入对象
            settings.CefCommandLineArgs.Add("disable-features", "EnableCefIntegration");

            Cef.Initialize(settings, performDependencyCheck: true, browserProcessHandler: null);

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                System.IO.File.WriteAllText("crash.log", args.ExceptionObject.ToString());
            };

            base.OnStartup(e);
        }
    }
}
