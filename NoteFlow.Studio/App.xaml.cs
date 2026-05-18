using System.Configuration;
using System.Data;
using System.Windows;

namespace NoteFlow.Studio
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                System.IO.File.WriteAllText("crash.log", args.ExceptionObject.ToString());
            };

            base.OnStartup(e);
        }
    }

}
