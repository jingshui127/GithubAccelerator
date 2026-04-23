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

            string iconPath = FindAppIcon();
            System.Drawing.Icon? icon = null;

            if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
            {
                try
                {
                    icon = new System.Drawing.Icon(iconPath);
                }
                catch
                {
                    icon = System.Drawing.SystemIcons.Application;
                }
            }
            else
            {
                icon = System.Drawing.SystemIcons.Application;
            }

            _trayIcon = new NotifyIcon
            {
                Icon = icon,
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

    private string? FindAppIcon()
    {
        var possiblePaths = new[]
        {
            "Assets/app-icon.ico",
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app-icon.ico"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GithubAccelerator.UI", "Assets", "app-icon.ico"),
            Path.Combine(Environment.CurrentDirectory, "Assets", "app-icon.ico")
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }
}
