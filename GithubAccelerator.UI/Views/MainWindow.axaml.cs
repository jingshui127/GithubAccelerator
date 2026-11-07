using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Controls.ApplicationLifetimes;
using GithubAccelerator.UI.Helpers;
using GithubAccelerator.UI.Services;
using GithubAccelerator.UI.ViewModels;
using System;
using System.Threading.Tasks;

namespace GithubAccelerator.UI.Views;

public partial class MainWindow : Window, IRestorableWindow
{
    private TrayIconManager? _trayIconManager;
    private bool _minimizeToTray = true;
    private bool _showTrayNotifications = true;
    private bool _isExiting = false;

    public MainWindow()
    {
        InitializeComponent();

        PropertyChanged += MainWindow_PropertyChanged;
        InitializeTrayIcon();
    }

    /// <summary>设置页改动后立即同步，不依赖重启。</summary>
    private void BindTraySettings(SettingsViewModel settings)
    {
        _minimizeToTray = settings.MinimizeToTray;
        _showTrayNotifications = settings.ShowTrayNotifications;
        settings.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(SettingsViewModel.MinimizeToTray):
                    _minimizeToTray = settings.MinimizeToTray;
                    break;
                case nameof(SettingsViewModel.ShowTrayNotifications):
                    _showTrayNotifications = settings.ShowTrayNotifications;
                    break;
            }
        };
    }

    private void InitializeTrayIcon()
    {
        _trayIconManager = new TrayIconManager();
        _trayIconManager.Initialize(
            onShowWindow: RestoreWindow,
            onExit: ExitApplication
        );
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // 第二个实例启动时通过命名事件请求"把窗口找回来"，走与托盘同一套恢复逻辑。
        SingleInstanceGuard.EnableActivationSignal(RestoreWindow);

        if (DataContext is MainWindowViewModel viewModel)
        {
            BindTraySettings(viewModel.Settings);

            // 提权重启后接续用户上次未完成的操作
            _ = viewModel.RunPendingLaunchIntentAsync();
        }
    }

    private void MainWindow_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == WindowStateProperty)
        {
            if (this.WindowState == WindowState.Minimized && _minimizeToTray)
            {
                this.Hide();
                this.ShowInTaskbar = false;
                if (_showTrayNotifications)
                {
                    _trayIconManager?.ShowBalloonTip("GitHub 加速器 Pro", "已最小化到系统托盘");
                }
            }
            else if (this.WindowState == WindowState.Normal)
            {
                this.ShowInTaskbar = true;
            }
        }
    }

    private void Window_Closing(object? sender, WindowClosingEventArgs e)
    {
        // 关闭按钮直接退出程序（最小化到托盘的行为在 WindowState 变化时处理）
        if (_isExiting) return;
        _isExiting = true;
        _trayIconManager?.Dispose();
    }

    /// <summary>
    /// 所有"让窗口回来"的入口共用：托盘左键单击/双击、托盘右键菜单、第二个实例启动。
    /// </summary>
    private void RestoreWindow()
    {
        WindowRestorer.Restore(this);
    }

    bool IRestorableWindow.IsVisible => IsVisible;

    WindowState IRestorableWindow.WindowState
    {
        get => WindowState;
        set => WindowState = value;
    }

    void IRestorableWindow.ShowWindow() => Show();

    void IRestorableWindow.ActivateWindow() => Activate();

    private void ExitApplication()
    {
        _isExiting = true;
        _trayIconManager?.Dispose();

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
        else
        {
            Close();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        SingleInstanceGuard.DisableActivationSignal();
        _trayIconManager?.Dispose();
    }
}