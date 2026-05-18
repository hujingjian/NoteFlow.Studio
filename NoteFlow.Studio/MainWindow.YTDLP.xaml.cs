using CefSharp;
using CefSharp.Handler;
using CefSharp.Wpf;
using NoteFlow.Studio.CefSharpHandlers;
using NoteFlow.Studio.Service;
using NoteFlow.Studio.Service.Interfaces;
using NoteFlow.Studio.Service.Models;
using NoteFlow.Studio.Service.VideoParsers;
using NoteFlow.Studio.Infrastructure.Logging;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace NoteFlow.Studio
{
    public partial class MainWindow : Window
    {

        private NoteFlow.Studio.Service.Providers.YtDlpService _ytdlpService = new NoteFlow.Studio.Service.Providers.YtDlpService();
        private async Task CheckYtDlpStatusAsync()
        {
            if (await _ytdlpService.IsAvailableAsync())
            {
                IconYtdlpStatus.Foreground = new SolidColorBrush(Colors.LimeGreen);
                BtnYtdlpAction.Content = "升级";
            }
            else
            {
                IconYtdlpStatus.Foreground = new SolidColorBrush(Colors.Red);
                BtnYtdlpAction.Content = "安装";
            }
        }

        private async void BtnYtdlpAction_Click(object sender, RoutedEventArgs e)
        {
            if (BtnYtdlpAction.Content?.ToString() == "安装")
            {
                BtnYtdlpAction.IsEnabled = false;
                ProgYtdlpDownload.Visibility = Visibility.Visible;
                ProgYtdlpDownload.Value = 0;
                BtnYtdlpAction.Content = "下载中...";
                try
                {
                    var progress = new Progress<double>(p => ProgYtdlpDownload.Value = p);
                    await _ytdlpService.InstallAsync(progress);
                    MessageBox.Show("yt-dlp 下载完成！", "提示");
                }
                catch (Exception ex)
                {
                    MessageBox.Show("下载失败: " + ex.Message, "错误");
                }
                finally
                {
                    ProgYtdlpDownload.Visibility = Visibility.Collapsed;
                    BtnYtdlpAction.IsEnabled = true;
                    await CheckYtDlpStatusAsync();
                }
            }
            else if (BtnYtdlpAction.Content?.ToString() == "升级")
            {
                BtnYtdlpAction.IsEnabled = false;
                BtnYtdlpAction.Content = "检查中...";
                try
                {
                    var result = await _ytdlpService.UpdateAsync();
                    if (result == "UP_TO_DATE")
                    {
                        MessageBox.Show("已是最新版本。", "提示");
                    }
                    else
                    {
                        MessageBox.Show("升级完成！\n\n" + result, "提示");
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("升级失败: " + ex.Message, "错误");
                }
                finally
                {
                    BtnYtdlpAction.IsEnabled = true;
                    await CheckYtDlpStatusAsync();
                }
            }
        }
    }
}