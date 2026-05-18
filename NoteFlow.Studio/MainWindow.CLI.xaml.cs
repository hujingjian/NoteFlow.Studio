using CefSharp;
using CefSharp.Handler;
using CefSharp.Wpf;
using NoteFlow.Studio.CefSharpHandlers;
using NoteFlow.Studio.Infrastructure.Logging;
using NoteFlow.Studio.Model.Config;
using NoteFlow.Studio.Service;
using NoteFlow.Studio.Service.Interfaces;
using NoteFlow.Studio.Service.Models;
using NoteFlow.Studio.Service.VideoParsers;
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
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool MoveWindow(IntPtr hwnd, int x, int y, int cx, int cy, bool repaint);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SetFocus(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SetActiveWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);



        private const int GWL_STYLE = -16;
        private const int GWL_EXSTYLE = -20;
        private const int WS_VISIBLE = 0x10000000;
        private const int WS_CHILD = 0x40000000;
        private const int WS_CAPTION = 0x00C00000;
        private const int WS_THICKFRAME = 0x00040000;
        private const int WS_BORDER = 0x00800000;
        private const int WS_EX_CLIENTEDGE = 0x00000200;
        private const int WS_EX_CONTROLPARENT = 0x00010000;
        private const int SW_SHOW = 5;

        private System.Diagnostics.Process _cliProcess;

        private void ForceRefocusConsole(IntPtr childHandle)
        {
            if (childHandle == IntPtr.Zero) return;
            uint wpfThreadId = GetCurrentThreadId();
            uint childThreadId = GetWindowThreadProcessId(childHandle, out _);

            if (wpfThreadId != childThreadId)
            {
                AttachThreadInput(wpfThreadId, childThreadId, true);
                FocusTerminal(childHandle);
                SetActiveWindow(childHandle);
                AttachThreadInput(wpfThreadId, childThreadId, false);
            }
            else
            {
                FocusTerminal(childHandle);
                SetActiveWindow(childHandle);
            }
        }
        void FocusTerminal(IntPtr childHandle)
        {
            ShowWindow(childHandle, SW_SHOW);
            SetForegroundWindow(childHandle);
            SetFocus(childHandle);
        }

        private async void BtnStartCli_Click(object sender, RoutedEventArgs e)
        {
            if (_cliProcess != null && !_cliProcess.HasExited)
            {
                if (MessageBox.Show("CLI 已经在运行中。是否要关闭当前终端并启动新的 Claude CLI？", "提示", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    try
                    {
                        _cliProcess.Kill();
                        _cliProcess.Dispose();
                    }
                    catch (Exception ex) 
                    { 
                        AppLogger.Error("关闭CLI进程异常", ex);
                    }
                    _cliProcess = null;
                }
                else
                {
                    return;
                }
            }

            var repo = new NoteFlow.Studio.Repository.Config.ConfigRepository();
            string startDir = repo.GetConfig(AppConfigKeys.ObsidianRootPath, "D:\\");

            _cliProcess = new System.Diagnostics.Process();
            _cliProcess.StartInfo.FileName = "cmd.exe";
            _cliProcess.StartInfo.Arguments = "/k claude"; 
            _cliProcess.StartInfo.WorkingDirectory = System.IO.Directory.Exists(startDir) ? startDir : "C:\\";
            _cliProcess.StartInfo.UseShellExecute = true; 
            _cliProcess.StartInfo.CreateNoWindow = false;
            _cliProcess.StartInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Minimized;

            try
            {
                _cliProcess.Start();
                
                while (_cliProcess.MainWindowHandle == IntPtr.Zero && !_cliProcess.HasExited)
                {
                    await Task.Delay(100);
                    _cliProcess.Refresh();
                }

                if (_cliProcess.HasExited) return;

                IntPtr childHandle = _cliProcess.MainWindowHandle;
                if (childHandle != IntPtr.Zero)
                {
                    var winFormsPanel = new System.Windows.Forms.Panel();
                    winFormsPanel.BackColor = System.Drawing.Color.Black;
                    winFormsPanel.BorderStyle = System.Windows.Forms.BorderStyle.None;
                    winFormsPanel.Margin = new System.Windows.Forms.Padding(0);
                    
                    TerminalHost.Child = winFormsPanel;
                    TerminalHost.Background = new SolidColorBrush(Colors.Black);
                    TerminalHost.Focusable = true;

                    // 使用底层跨线程输入附加，强行要求焦点
                    TerminalHost.PreviewMouseLeftButtonDown += (s, ev) => 
                    { 
                        //TerminalHost.Focus(); 
                        ForceRefocusConsole(childHandle);
                        ev.Handled = true;
                    };
                    //TerminalHost.GotKeyboardFocus += (s, ev) => ForceRefocusConsole(childHandle);

                    TerminalHost.GotFocus += (s, ev) => FocusTerminal(childHandle);

                    // 当主窗口被切走再回来(ALT+TAB) 或点击其他部分再回来时，也尽可能捕捉恢复光标
                    //this.Activated += (s, ev) => ForceRefocusConsole(childHandle);

                    SetParent(childHandle, winFormsPanel.Handle);

                    // 剥离原有的边框、标题栏等样式
                    int style = GetWindowLong(childHandle, GWL_STYLE);
                    style &= ~WS_CAPTION;
                    style &= ~WS_BORDER;
                    style &= ~WS_THICKFRAME;
                    style |= WS_VISIBLE | WS_CHILD;
                    SetWindowLong(childHandle, GWL_STYLE, style);

                    int exStyle = GetWindowLong(childHandle, GWL_EXSTYLE);
                    exStyle &= ~WS_EX_CLIENTEDGE;
                    exStyle |= WS_EX_CONTROLPARENT; // 允许父窗口导航到子窗口焦点
                    SetWindowLong(childHandle, GWL_EXSTYLE, exStyle);

                    winFormsPanel.Resize += (s, args) =>
                    {
                        MoveWindow(childHandle, 0, 0, winFormsPanel.Width, winFormsPanel.Height, true);
                    };
                    winFormsPanel.MouseClick += (s, ev) => FocusTerminal(childHandle);

                    //winFormsPanel.GotFocus += (s, ev) => FocusTerminal(childHandle);

                    MoveWindow(childHandle, 0, 0, winFormsPanel.Width, winFormsPanel.Height, true);

                    var source = (HwndSource)PresentationSource.FromVisual(TerminalHost);
                    if (source != null)
                    {
                        // 添加底层的 Win32 消息钩子
                        source.AddHook(ChildWindowMessageHook);
                    }

                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(200);
                        Dispatcher.Invoke(() => ForceRefocusConsole(childHandle));
                    });
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error($"启动CLI失败: {ex.Message}", ex);
                MessageBox.Show("启动CLI失败: " + ex.Message);
            }
        }
        // 定义钩子处理函数
        private IntPtr ChildWindowMessageHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // 0x0210 是 WM_PARENTNOTIFY，当子窗口发生鼠标点击时会发给父窗口
            const int WM_PARENTNOTIFY = 0x0210;
            const int WM_LBUTTONDOWN = 0x0201;

            if (msg == WM_PARENTNOTIFY)
            {
                // 检查是否是鼠标左键按下
                if ((wParam.ToInt32() & 0xFFFF) == WM_LBUTTONDOWN)
                {
                    if (_cliProcess != null && !_cliProcess.HasExited)
                    {
                        // 这里调用你的聚焦逻辑
                        ForceRefocusConsole(_cliProcess.MainWindowHandle);
                    }
                }
            }
            return IntPtr.Zero;
        }
    }
}