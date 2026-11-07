using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GithubAccelerator.UI.ViewModels;

/// <summary>首启引导里的网络检测状态。</summary>
public enum FirstRunNetworkState
{
    Checking,
    Reachable,
    Slow,
    Unreachable
}

/// <summary>首启引导里点击"开始加速"后的结果，用于把失败原因翻译成人话。</summary>
public enum FirstRunStartKind
{
    Success,
    ElevationDenied,
    NetworkUnavailable,
    MirrorUnavailable,
    WriteFailed,
    Incomplete
}

/// <summary>引导关闭时的最终结果（Started/Skipped 才视为"引导完成"）。</summary>
public enum FirstRunGuideResult
{
    None,
    Started,
    Skipped
}

public sealed record FirstRunNetworkResult(bool Reachable, long LatencyMs, string? Reason = null);

public sealed record FirstRunStartResult(FirstRunStartKind Kind, string? Detail = null);

/// <summary>
/// 首启引导：一句话说明用途 + 网络状态 + 一个"开始加速"按钮。
/// 不自动写系统配置；写入只发生在用户点击按钮之后。
/// </summary>
public partial class FirstRunGuideViewModel : ObservableObject
{
    private readonly Func<Task<FirstRunNetworkResult>>? _probe;
    private readonly Func<Task<FirstRunStartResult>>? _start;
    private readonly Action<bool, bool>? _saveOptions;

    public FirstRunGuideViewModel(
        Func<Task<FirstRunNetworkResult>>? probe = null,
        Func<Task<FirstRunStartResult>>? start = null,
        Action<bool, bool>? saveOptions = null)
    {
        _probe = probe;
        _start = start;
        _saveOptions = saveOptions;
    }

    [ObservableProperty]
    private FirstRunNetworkState _networkState = FirstRunNetworkState.Checking;

    [ObservableProperty]
    private string _networkText = "正在检测当前网络…";

    [ObservableProperty]
    private bool _isChecking;

    [ObservableProperty]
    private bool _isStarting;

    [ObservableProperty]
    private string? _failureText;

    [ObservableProperty]
    private string? _failureActionText;

    [ObservableProperty]
    private FirstRunGuideResult _result = FirstRunGuideResult.None;

    /// <summary>开机自启，默认关闭。</summary>
    [ObservableProperty]
    private bool _autoStart = false;

    /// <summary>匿名诊断信息，默认关闭。</summary>
    [ObservableProperty]
    private bool _diagnosticsEnabled = false;

    public bool HasFailure => !string.IsNullOrEmpty(FailureText);

    public bool CanStart => !IsChecking && !IsStarting;

    public bool CanCheck => !IsChecking;

    public string StartButtonText => IsStarting ? "正在开启…" : "开启网页加速";

    public bool IsNetworkHealthy => NetworkState is FirstRunNetworkState.Reachable or FirstRunNetworkState.Slow;

    public string NetworkIcon => NetworkState switch
    {
        FirstRunNetworkState.Checking => "⏳",
        FirstRunNetworkState.Reachable => "✅",
        FirstRunNetworkState.Slow => "⚠️",
        _ => "⛔"
    };

    public string NetworkColor => NetworkState switch
    {
        FirstRunNetworkState.Checking => "#9E9E9E",
        FirstRunNetworkState.Reachable => "#2E7D32",
        FirstRunNetworkState.Slow => "#EF6C00",
        _ => "#C62828"
    };

    partial void OnFailureTextChanged(string? value) => OnPropertyChanged(nameof(HasFailure));

    partial void OnIsCheckingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanCheck));
        OnPropertyChanged(nameof(CanStart));
    }

    partial void OnIsStartingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(StartButtonText));
    }

    partial void OnNetworkStateChanged(FirstRunNetworkState value)
    {
        OnPropertyChanged(nameof(IsNetworkHealthy));
        OnPropertyChanged(nameof(NetworkIcon));
        OnPropertyChanged(nameof(NetworkColor));
    }

    /// <summary>检测当前网络（打开引导时自动调用一次，也可手动重新检测）。</summary>
    [RelayCommand]
    public async Task CheckNetworkAsync()
    {
        if (IsChecking) return;

        IsChecking = true;
        NetworkState = FirstRunNetworkState.Checking;
        NetworkText = "正在检测当前网络…";

        try
        {
            var result = _probe is null
                ? new FirstRunNetworkResult(false, 0)
                : await _probe();

            if (result.Reachable && result.LatencyMs >= 800)
            {
                NetworkState = FirstRunNetworkState.Slow;
                NetworkText = $"网络已连通，但比较慢（约 {result.LatencyMs / 1000.0:F1} 秒）。开启加速后通常会明显变快。";
            }
            else if (result.Reachable)
            {
                NetworkState = FirstRunNetworkState.Reachable;
                NetworkText = $"网络正常，GitHub 当前访问约 {result.LatencyMs} 毫秒。";
            }
            else
            {
                NetworkState = FirstRunNetworkState.Unreachable;
                NetworkText = "暂时连不上 GitHub，可能是网络波动，或当前网络限制了访问。";
                ShowFailure(FirstRunStartKind.NetworkUnavailable);
            }
        }
        catch (Exception)
        {
            NetworkState = FirstRunNetworkState.Unreachable;
            NetworkText = "网络检测没有完成，可能是网络波动或安全软件拦截。";
            ShowFailure(FirstRunStartKind.NetworkUnavailable);
        }
        finally
        {
            IsChecking = false;
        }
    }

    /// <summary>用户点击"开始加速"：这时才会真正开始写入流程。</summary>
    [RelayCommand]
    public async Task StartAsync()
    {
        if (IsStarting || IsChecking) return;

        ClearFailure();
        _saveOptions?.Invoke(AutoStart, DiagnosticsEnabled);

        IsStarting = true;
        try
        {
            var result = _start is null
                ? new FirstRunStartResult(FirstRunStartKind.WriteFailed)
                : await _start();

            if (result.Kind == FirstRunStartKind.Success)
            {
                Result = FirstRunGuideResult.Started;
                return;
            }

            ShowFailure(result.Kind);
        }
        catch (Exception)
        {
            ShowFailure(FirstRunStartKind.WriteFailed);
        }
        finally
        {
            IsStarting = false;
        }
    }

    /// <summary>跳过引导：不写任何系统文件，稍后可从左侧「帮助与支持」重新进入。</summary>
    public void Skip()
    {
        _saveOptions?.Invoke(AutoStart, DiagnosticsEnabled);
        Result = FirstRunGuideResult.Skipped;
    }

    public void ClearFailure()
    {
        FailureText = null;
        FailureActionText = null;
    }

    private void ShowFailure(FirstRunStartKind kind)
    {
        switch (kind)
        {
            case FirstRunStartKind.ElevationDenied:
                FailureText = "没有获得管理员权限，系统设置没有被修改。";
                FailureActionText = "再次点「开始加速」会重新弹出权限确认；也可以先跳过，稍后在主界面手动开启。";
                break;
            case FirstRunStartKind.NetworkUnavailable:
                FailureText = "连不上加速服务，多半是当前网络断开，或所在网络限制了外部访问。";
                FailureActionText = "检查网络后点「重新检测」，再试一次。";
                break;
            case FirstRunStartKind.MirrorUnavailable:
                FailureText = "加速节点暂时不可达，可能被网络限制，或节点正在维护。";
                FailureActionText = "点「重新检测」确认网络后重试；仍不行就到主界面手动选择数据源。";
                break;
            case FirstRunStartKind.WriteFailed:
                FailureText = "写入系统设置时出了问题，文件没有被修改。";
                FailureActionText = "重试一次；若仍失败，请尝试以管理员身份运行本程序。";
                break;
            default:
                FailureText = "这次操作没有完成，系统设置没有被修改。";
                FailureActionText = "稍等几秒再点「开始加速」即可。";
                break;
        }
    }
}
