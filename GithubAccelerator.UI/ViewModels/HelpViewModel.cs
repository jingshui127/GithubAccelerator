using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GithubAccelerator.UI.ViewModels;

/// <summary>
/// 「帮助与支持」统一入口：新手引导、问题报告（诊断包）、日志、关于与联系都在这里。
/// 原来的「使用指南」「日志查看」、侧栏联系方式、关于窗口入口都合并到此页。
/// </summary>
public partial class HelpViewModel : ObservableObject
{
    private readonly MainWindowViewModel _host;
    private readonly SettingsViewModel _settings;

    public HelpViewModel(
        MainWindowViewModel host,
        SettingsViewModel settings,
        LogViewerViewModel logViewer)
    {
        _host = host;
        _settings = settings;
        LogViewer = logViewer;
        VersionText = host.AppVersion;
    }

    public LogViewerViewModel LogViewer { get; }

    public string VersionText { get; }

    public string ContactText => "科控物联 · QQ: 2492123056 · 微信公众号：科控物联";

    [RelayCommand]
    private void ShowGuide() => _host.ShowQuickGuideCommand.Execute(null);

    [RelayCommand]
    private void ShowAbout() => _host.ShowAboutCommand.Execute(null);

    [RelayCommand]
    private void OpenDiagnostics() => _settings.OpenDiagnosticsBundleCommand.Execute(null);
}
