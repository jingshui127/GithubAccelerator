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

        // 诊断信息上报默认关闭，仅当用户在引导/设置页显式开启后才连接星尘监控
        DiagnosticsSettings.Load();
        StardustMonitorService.Instance.Enabled = DiagnosticsSettings.Enabled;
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