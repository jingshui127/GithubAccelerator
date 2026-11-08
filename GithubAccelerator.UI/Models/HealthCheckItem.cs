using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GithubAccelerator.UI.Models;

/// <summary>
/// GitHub 关键子域健康检查项。
/// 用于诊断"测速很快但浏览器打不开"这类局部子域失败的问题。
/// </summary>
public partial class HealthCheckItem : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    /// <summary>子域，例如 github.com、raw.githubusercontent.com</summary>
    [ObservableProperty]
    private string _host = string.Empty;

    /// <summary>实际探测 URL</summary>
    [ObservableProperty]
    private string _probeUrl = string.Empty;

    /// <summary>作用说明</summary>
    [ObservableProperty]
    private string _description = string.Empty;

    /// <summary>最近一次探测的延迟（毫秒，-1 表示失败）</summary>
    [ObservableProperty]
    private long _latencyMs = -1;

    /// <summary>状态：OK / SLOW / TIMEOUT / FAIL / PENDING</summary>
    [ObservableProperty]
    private string _status = "PENDING";

    /// <summary>最近探测时间</summary>
    [ObservableProperty]
    private DateTime _lastCheckTime;

    /// <summary>错误信息（连接失败时显示）</summary>
    [ObservableProperty]
    private string _errorMessage = string.Empty;

    /// <summary>失败时保证有一句可读说明，不出现空白或裸红叉。</summary>
    public string ErrorMessageText => string.IsNullOrWhiteSpace(ErrorMessage)
        ? "探测失败，但没有返回具体原因；可重试或换线路。"
        : ErrorMessage;

    public string LatencyText => IsCanceled
        ? "已取消"
        : IsPending
            ? "— 未检测"
            : LatencyMs switch
            {
                < 0 => Status == "TIMEOUT" ? "⏱ 超时" : "❌ 失败",
                < 1000 => $"{LatencyMs} ms",
                _ => $"{LatencyMs / 1000.0:F1} s"
            };

    /// <summary>延迟文本颜色：未检测为中性灰，只有检测完成后才用成功/失败色。</summary>
    public Avalonia.Media.IBrush LatencyBrush => IsPending || IsCanceled
        ? PendingBrush
        : IsOk
            ? OkBrush
            : IsSlow
                ? SlowBrush
                : FailBrush;

    private static readonly Avalonia.Media.IBrush PendingBrush =
        new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#9E9E9E"));
    private static readonly Avalonia.Media.IBrush OkBrush =
        new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#4CAF50"));
    private static readonly Avalonia.Media.IBrush SlowBrush =
        new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FFC107"));
    private static readonly Avalonia.Media.IBrush FailBrush =
        new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FF5722"));

    public bool IsOk => Status == "OK";
    public bool IsSlow => Status == "SLOW";
    public bool IsFail => Status == "TIMEOUT" || Status == "FAIL";
    public bool IsCanceled => Status == "CANCELED";
    public bool IsPending => !IsOk && !IsSlow && !IsFail && !IsCanceled;

    partial void OnStatusChanged(string value)
    {
        OnPropertyChanged(nameof(IsOk));
        OnPropertyChanged(nameof(IsSlow));
        OnPropertyChanged(nameof(IsFail));
        OnPropertyChanged(nameof(IsCanceled));
        OnPropertyChanged(nameof(IsPending));
        OnPropertyChanged(nameof(LatencyText));
        OnPropertyChanged(nameof(LatencyBrush));
    }

    partial void OnLatencyMsChanged(long value)
    {
        OnPropertyChanged(nameof(LatencyText));
    }

    partial void OnErrorMessageChanged(string value)
    {
        OnPropertyChanged(nameof(ErrorMessageText));
    }
}

/// <summary>
/// 预定义的 GitHub 关键子域清单。
/// </summary>
public static class GitHubSubDomains
{
    public static readonly IReadOnlyList<(string Name, string Host, string ProbeUrl, string Description)> Items = new[]
    {
        ("主页", "github.com", "https://github.com", "网站首页（仓库浏览）"),
        ("Raw 内容", "raw.githubusercontent.com", "https://raw.githubusercontent.com/", "查看原始文件（README、代码）"),
        ("对象存储", "objects.githubusercontent.com", "https://objects.githubusercontent.com/", "Release / 大文件下载"),
        ("头像", "avatars.githubusercontent.com", "https://avatars.githubusercontent.com/", "用户头像、仓库图标"),
        ("API", "api.github.com", "https://api.github.com", "REST API 调用（IDE 集成）"),
        ("Pages", "*.github.io", "https://github.io", "GitHub Pages 站点"),
    };
}
