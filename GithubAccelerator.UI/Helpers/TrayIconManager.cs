using System;
using System.IO;
using System.Windows.Forms;
using Avalonia.Threading;
using ToolTipIcon = System.Windows.Forms.ToolTipIcon;

namespace GithubAccelerator.UI.Helpers;

public class TrayIconManager : IDisposable
{
    private NotifyIcon? _trayIcon;
    private bool _disposed;
    private Action? _onShowWindow;
    private Action? _onExit;

    public void Initialize(Action? onShowWindow = null, Action? onExit = null)
    {
        try
        {
            _onShowWindow = onShowWindow;
            _onExit = onExit;

            var iconPath = "Assets/app-icon.ico";
            if (!File.Exists(iconPath))
            {
                iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, iconPath);
            }

            if (File.Exists(iconPath))
            {
                _trayIcon = new NotifyIcon
                {
                    Icon = new System.Drawing.Icon(iconPath),
                    Text = "GitHub 加速器 Pro",
                    Visible = true
                };

                var contextMenu = new ContextMenuStrip();

                var showItem = new ToolStripMenuItem("显示窗口");
                showItem.Click += (s, e) =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        _onShowWindow?.Invoke();
                    });
                };

                var exitItem = new ToolStripMenuItem("退出");
                exitItem.Click += (s, e) =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        _onExit?.Invoke();
                    });
                };

                contextMenu.Items.Add(showItem);
                contextMenu.Items.Add(new ToolStripSeparator());
                contextMenu.Items.Add(exitItem);

                _trayIcon.ContextMenuStrip = contextMenu;

                _trayIcon.MouseClick += (s, e) =>
                {
                    if (e.Button == MouseButtons.Left && e.Clicks == 2)
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            _onShowWindow?.Invoke();
                        });
                    }
                };
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"初始化托盘图标失败: {ex.Message}");
        }
    }

    public void ShowBalloonTip(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
    {
        _trayIcon?.ShowBalloonTip(3000, title, message, icon);
    }

    public void SetIcon(string tooltip)
    {
        if (_trayIcon != null)
        {
            _trayIcon.Text = tooltip;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _trayIcon?.Dispose();
            _disposed = true;
        }
    }
}
