using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace NoteFlow.Studio.Views
{
    public partial class AboutMeView : UserControl
    {
        public AboutMeView()
        {
            InitializeComponent();
        }

        private void Github_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://github.com/hujingjian/NoteFlow.Studio");
        }

        private void Xiaohongshu_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://www.xiaohongshu.com/user/profile/685d37e3000000001b021fe7");
        }

        private void Bilibili_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://space.bilibili.com/1701627402");
        }

        private void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"无法打开链接：{ex.Message}");
            }
        }
    }
}