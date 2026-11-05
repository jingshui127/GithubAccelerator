using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using GithubAccelerator.UI.ViewModels;
using GithubAccelerator.UI.Views;
using GithubAccelerator.UI.Services;

namespace GithubAccelerator.UI;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        ThemeManager.Initialize();

        LogService.Instance.Initialize();

        // 启动星尘监控，用于改进 APP（连接 http://47.113.219.65:6600）
        StardustMonitorService.Instance.Initialize();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}