using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GithubAccelerator.UI.ViewModels;

/// <summary>
/// 「检查与修复」统一入口：合并原来的「依赖站点检查」「官网访问诊断」「网络监控」三页。
/// 只读检测，不改 Hosts / DNS / 代理；结论、明细、单次响应统计都在此页。
/// </summary>
public partial class CheckAndRepairViewModel : ObservableObject
{
    public CheckAndRepairViewModel(
        HealthCheckViewModel health,
        OfficialSiteDiagnosticsViewModel officialSite,
        NetworkMonitorViewModel monitor)
    {
        Health = health;
        OfficialSite = officialSite;
        Latency = monitor.GitHubLatency;
    }

    public HealthCheckViewModel Health { get; }

    public OfficialSiteDiagnosticsViewModel OfficialSite { get; }

    public GitHubLatencyViewModel Latency { get; }

    [ObservableProperty]
    private bool _isRunningAll;

    public bool IsNotRunningAll => !IsRunningAll;

    partial void OnIsRunningAllChanged(bool value) => OnPropertyChanged(nameof(IsNotRunningAll));

    /// <summary>一次点按同时刷新依赖站点与官网诊断，两者都是只读请求。</summary>
    [RelayCommand]
    private async Task RunAll()
    {
        if (IsRunningAll) return;

        IsRunningAll = true;
        try
        {
            await Task.WhenAll(
                Health.RunCheckCommand.ExecuteAsync(null),
                OfficialSite.RefreshCommand.ExecuteAsync(null));
        }
        finally
        {
            IsRunningAll = false;
        }
    }

    // ---------- Hosts 写入自检 ----------

    /// <summary>由主窗口注入：内部走提权 + 临时副本写入 + 校验 + 回滚。</summary>
    public Func<Task>? RunWriteSelfTestAsync { get; set; }

    public Func<string, Task>? SetClipboardTextAsync { get; set; }

    /// <summary>自检进行/完成时把“写入自检”页签自动切到前台，让逐阶段进展可见。</summary>
    [ObservableProperty]
    private bool _isSelfTestTabSelected;

    [ObservableProperty]
    private bool _isSelfTestRunning;

    [ObservableProperty]
    private string? _selfTestStatus;

    [ObservableProperty]
    private string? _selfTestReport;

    public bool IsSelfTestNotRunning => !IsSelfTestRunning;

    public bool HasSelfTestStatus => !string.IsNullOrWhiteSpace(SelfTestStatus);

    public bool HasSelfTestReport => !string.IsNullOrWhiteSpace(SelfTestReport);

    partial void OnIsSelfTestRunningChanged(bool value) => OnPropertyChanged(nameof(IsSelfTestNotRunning));

    partial void OnSelfTestStatusChanged(string? value) => OnPropertyChanged(nameof(HasSelfTestStatus));

    partial void OnSelfTestReportChanged(string? value) => OnPropertyChanged(nameof(HasSelfTestReport));

    [RelayCommand]
    private async Task RunWriteSelfTest()
    {
        if (IsSelfTestRunning || RunWriteSelfTestAsync == null) return;

        IsSelfTestRunning = true;
        try
        {
            await RunWriteSelfTestAsync();
        }
        catch (Exception ex)
        {
            SelfTestStatus = $"自检异常：{ex.Message}";
        }
        finally
        {
            IsSelfTestRunning = false;
        }
    }

    [RelayCommand]
    private async Task CopySelfTestReport()
    {
        if (string.IsNullOrWhiteSpace(SelfTestReport)) return;
        if (SetClipboardTextAsync == null)
        {
            SelfTestStatus = "剪贴板未初始化，无法复制";
            return;
        }

        await SetClipboardTextAsync(SelfTestReport);
        SelfTestStatus = "自检报告已复制到剪贴板";
    }
}
