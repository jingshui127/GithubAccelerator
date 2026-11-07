using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GithubAccelerator.UI.ViewModels;

/// <summary>
/// 「检查与修复」统一入口：合并原来的「依赖站点检查」「官网访问诊断」「网络监控」三页。
/// 只读检测，不改 Hosts / DNS / 代理；结论、明细、连续采样都在此页。
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
}
