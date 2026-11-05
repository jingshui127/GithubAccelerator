using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using GithubAccelerator.UI.Services;

namespace GithubAccelerator.UI.Views;

public partial class AboutWindow : Window
{
    private Version _currentVersion = new(1, 0, 0);

    public AboutWindow()
    {
        InitializeComponent();
        LoadVersionInfo();
    }

    private void LoadVersionInfo()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        _currentVersion = version ?? new Version(1, 0, 0);
        VersionText.Text = $"v{version?.Major ?? 1}.{version?.Minor ?? 0}.{version?.Build ?? 0}";

        // 单文件发布时 Assembly.Location 为空，改用进程路径，避免 IL3000 与空白日期。
        var exePath = Environment.ProcessPath;
        BuildDateText.Text = exePath != null && File.Exists(exePath)
            ? File.GetLastWriteTime(exePath).ToString("yyyy.MM.dd")
            : "未知";
    }

    /// <summary>手动版本检查：只发一次匿名请求，5 秒超时，失败只更新这一行文字。</summary>
    private async void CheckUpdate_Click(object? sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        UpdateStatusText.Text = "正在检查…";
        try
        {
            var result = await UpdateCheckService.Instance.CheckAsync(_currentVersion);
            UpdateStatusText.Text = result.Message;
            OpenReleasesButton.IsVisible =
                result.Status is UpdateCheckStatus.UpdateAvailable or UpdateCheckStatus.NoReleaseInfo;
        }
        finally
        {
            CheckUpdateButton.IsEnabled = true;
        }
    }

    private void OpenReleases_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(UpdateCheckService.ReleasesPageUrl)
            {
                UseShellExecute = true
            });
        }
        catch
        {
            UpdateStatusText.Text = "无法打开浏览器，请手动访问："
                                    + UpdateCheckService.ReleasesPageUrl;
        }
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
