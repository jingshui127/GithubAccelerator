using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Controls.ApplicationLifetimes;
using GithubAccelerator.UI.Helpers;
using GithubAccelerator.UI.ViewModels;
using System;
using System.IO;

namespace GithubAccelerator.UI.Views;

public partial class MainWindow : Window
{
    private TrayIconManager? _trayIconManager;
    private bool _minimizeToTray = true;
    private bool _isExiting = false;

    public MainWindow()
    {
        InitializeComponent();

        PropertyChanged += MainWindow_PropertyChanged;

        LoadMinimizeToTraySetting();
        InitializeTrayIcon();
    }

    private void LoadMinimizeToTraySetting()
    {
        try
        {
            var settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "GithubAccelerator",
                "settings.json");

            if (File.Exists(settingsPath))
            {
                var json = File.ReadAllText(settingsPath);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("MinimizeToTray", out var element))
                {
                    _minimizeToTray = element.GetBoolean();
                }
            }
        }
        catch
        {
            _minimizeToTray = true;
        }
    }

    private void InitializeTrayIcon()
    {
        _trayIconManager = new TrayIconManager();
        _trayIconManager.Initialize(
            onShowWindow: ShowWindow,
            onExit: ExitApplication
        );
    }

    private void MainWindow_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == WindowStateProperty)
        {
            if (this.WindowState == WindowState.Minimized && _minimizeToTray)
            {
                this.Hide();
                this.ShowInTaskbar = false;
                _trayIconManager?.ShowBalloonTip("GitHub 加速器 Pro", "已最小化到系统托盘");
            }
            else if (this.WindowState == WindowState.Normal)
            {
                this.ShowInTaskbar = true;
            }
        }
    }

    private void Window_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (_isExiting) return;

        if (_minimizeToTray)
        {
            e.Cancel = true;
            this.Hide();
            this.ShowInTaskbar = false;
            _trayIconManager?.ShowBalloonTip("GitHub 加速器 Pro", "程序已在后台运行");
        }
    }

    private void ShowWindow()
    {
        this.Show();
        this.ShowInTaskbar = true;
        this.WindowState = WindowState.Normal;
        this.Activate();
    }

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
        _trayIconManager?.Dispose();
    }
}