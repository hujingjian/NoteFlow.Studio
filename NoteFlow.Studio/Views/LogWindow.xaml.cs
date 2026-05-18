using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using NoteFlow.Studio.Infrastructure.Logging;

namespace NoteFlow.Studio.Views
{
    public partial class LogWindow : Window
    {
        public LogWindow()
        {
            InitializeComponent();
            LogListView.DataContext = AppLogger.MemoryLogs;
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            AppLogger.ClearMemoryLogs();
        }

        private void BtnOpenDir_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = AppLogger.LogDirectory,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法打开目录: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    public class LogLevelToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string level)
            {
                switch (level.ToUpper())
                {
                    case "INFO": return Brushes.LimeGreen;
                    case "ERROR": return Brushes.Red;
                    case "WARN": return Brushes.Yellow;
                    case "DEBUG": return Brushes.Gray;
                    case "TRACE": return Brushes.DarkGray;
                    default: return Brushes.White;
                }
            }
            return Brushes.White;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}
