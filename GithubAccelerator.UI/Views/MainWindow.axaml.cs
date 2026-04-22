using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using GithubAccelerator.UI.Helpers;
using GithubAccelerator.UI.ViewModels;
using System;

namespace GithubAccelerator.UI.Views;

public partial class MainWindow : Window
{
    private TrayIconManager? _trayIconManager;
    private bool _isMinimizedToTray;

    public MainWindow()
    {
        InitializeComponent();
        
        this.Closing += MainWindow_Closing;
        this.PropertyChanged += MainWindow_PropertyChanged;
        
        InitializeTrayIcon();
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
        if (e.Property == WindowStateProperty && this.WindowState == WindowState.Minimized)
        {
            var viewModel = DataContext as MainWindowViewModel;
            if (viewModel?.MinimizeToTray == true && !_isMinimizedToTray)
            {
                Hide();
                _isMinimizedToTray = true;
                _trayIconManager?.ShowBalloonTip("GitHub 加速器 Pro", "已最小化到系统托盘");
            }
        }
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var viewModel = DataContext as MainWindowViewModel;
        if (viewModel?.MinimizeToTray == true)
        {
            e.Cancel = true;
            Hide();
            _isMinimizedToTray = true;
            _trayIconManager?.ShowBalloonTip("GitHub 加速器 Pro", "程序已在后台运行");
        }
    }

    private void ShowWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        _isMinimizedToTray = false;
    }

    private void ExitApplication()
    {
        _trayIconManager?.Dispose();
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _trayIconManager?.Dispose();
    }
}