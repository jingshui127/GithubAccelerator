using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GithubAccelerator.UI.Models;
using GithubAccelerator.UI.Services;

namespace GithubAccelerator.UI.ViewModels;

public partial class HealthCheckViewModel : ObservableObject
{
    private readonly HealthCheckService _service = HealthCheckService.Instance;
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private ObservableCollection<HealthCheckItem> _items = new();

    [ObservableProperty]
    private bool _isRunning;

    public bool IsNotRunning => !IsRunning;

    partial void OnIsRunningChanged(bool value) => OnPropertyChanged(nameof(IsNotRunning));

    [ObservableProperty]
    private DateTime _lastCheckTime;

    [ObservableProperty]
    private int _okCount;

    [ObservableProperty]
    private int _slowCount;

    [ObservableProperty]
    private int _failCount;

    [ObservableProperty]
    private string _statusMessage = "点击「开始检测」诊断 GitHub 关键子域可达性";

    [ObservableProperty]
    private string _overallStatus = "未知";

    [ObservableProperty]
    private bool _canCopy = false;

    /// <summary>确定进度：已完成 X/共 N 项（不编造百分比）。</summary>
    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private bool _isProgressVisible = false;

    /// <summary>是否已经完成过至少一次检测：未检测时计数与时间一律显示“—”。</summary>
    public bool HasAnyResult => LastCheckTime != default;

    public string LastCheckTimeText => HasAnyResult
        ? LastCheckTime.ToString("yyyy-MM-dd HH:mm:ss")
        : "— 未检测";

    public string OkCountText => HasAnyResult ? OkCount.ToString() : "—";
    public string SlowCountText => HasAnyResult ? SlowCount.ToString() : "—";
    public string FailCountText => HasAnyResult ? FailCount.ToString() : "—";

    partial void OnLastCheckTimeChanged(DateTime value)
    {
        OnPropertyChanged(nameof(HasAnyResult));
        OnPropertyChanged(nameof(LastCheckTimeText));
        OnPropertyChanged(nameof(OkCountText));
        OnPropertyChanged(nameof(SlowCountText));
        OnPropertyChanged(nameof(FailCountText));
    }

    partial void OnOkCountChanged(int value) => OnPropertyChanged(nameof(OkCountText));
    partial void OnSlowCountChanged(int value) => OnPropertyChanged(nameof(SlowCountText));
    partial void OnFailCountChanged(int value) => OnPropertyChanged(nameof(FailCountText));

    public HealthCheckViewModel()
    {
        // 初始化空白条目（避免首次空白）
        foreach (var def in GitHubSubDomains.Items)
        {
            Items.Add(new HealthCheckItem
            {
                Name = def.Name,
                Host = def.Host,
                ProbeUrl = def.ProbeUrl,
                Description = def.Description,
                Status = "PENDING"
            });
        }
    }

    [RelayCommand]
    private async Task RunCheck()
    {
        if (IsRunning) return;

        // 取消后要回到上一个稳定状态：先保存当前结果快照，成功后才替换。
        var snapshot = Items.Select(CloneItem).ToList();
        var snapshotOverall = OverallStatus;
        var snapshotTime = LastCheckTime;
        var snapshotCanCopy = CanCopy;

        IsRunning = true;
        StatusMessage = "正在探测全部子域...";
        ProgressText = $"已完成 0/共 {GitHubSubDomains.Items.Count} 项";
        IsProgressVisible = true;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            var results = await _service.ProbeAllAsync(
                ct,
                (done, total) => Avalonia.Threading.Dispatcher.UIThread.Post(
                    () => ProgressText = $"已完成 {done}/共 {total} 项"));

            if (ct.IsCancellationRequested)
            {
                RestoreSnapshot(snapshot, snapshotOverall, snapshotTime, snapshotCanCopy);
                return;
            }

            Items.Clear();
            foreach (var item in results)
            {
                Items.Add(item);
            }
            LastCheckTime = DateTime.Now;
            UpdateSummary();

            var failedHosts = Items.Where(x => x.IsFail).Select(x => x.Host).ToList();
            if (FailCount == 0 && SlowCount == 0)
            {
                OverallStatus = "✅ 全部正常";
                StatusMessage = "所有关键子域访问正常";
            }
            else if (FailCount > 0)
            {
                OverallStatus = $"❌ {FailCount} 个子域不可达";
                StatusMessage = $"以下子域访问失败：{string.Join("、", failedHosts)}。建议切换 Hosts 源或重新应用。";
                StardustMonitorService.Instance.ReportError("GitHubHealthCheckFailed", _service.Summarize(Items));
            }
            else
            {
                OverallStatus = $"⚠️ {SlowCount} 个子域较慢";
                StatusMessage = "部分子域响应较慢，但可达。";
                StardustMonitorService.Instance.ReportInfo("GitHubHealthCheckSlow", _service.Summarize(Items));
            }

            StardustMonitorService.Instance.ReportInfo("GitHubHealthCheck", _service.Summarize(Items));

            CanCopy = true;
        }
        catch (OperationCanceledException)
        {
            // 取消不是失败：回到取消前的状态，不写任何结果。
            RestoreSnapshot(snapshot, snapshotOverall, snapshotTime, snapshotCanCopy);
        }
        catch (Exception ex)
        {
            StatusMessage = $"检测失败：{ex.Message}";
            StardustMonitorService.Instance.ReportError("GitHubHealthCheckException", ex.Message, ex);
        }
        finally
        {
            IsRunning = false;
            IsProgressVisible = false;
            ProgressText = string.Empty;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void RestoreSnapshot(
        List<HealthCheckItem> snapshot, string overall, DateTime time, bool canCopy)
    {
        Items.Clear();
        foreach (var item in snapshot)
        {
            Items.Add(item);
        }
        StatusMessage = "已取消检测：保留上一次结果";
        OverallStatus = overall;
        LastCheckTime = time;
        CanCopy = canCopy;
    }

    private static HealthCheckItem CloneItem(HealthCheckItem source) => new()
    {
        Name = source.Name,
        Host = source.Host,
        ProbeUrl = source.ProbeUrl,
        Description = source.Description,
        LatencyMs = source.LatencyMs,
        Status = source.Status,
        LastCheckTime = source.LastCheckTime,
        ErrorMessage = source.ErrorMessage
    };

    /// <summary>
    /// 由 View 层设置的剪贴板设置委托。
    /// ViewModel 不直接依赖 Avalonia UI 层，通过委托实现复制。
    /// </summary>
    public Func<string, Task>? SetClipboardTextAsync { get; set; }

    [RelayCommand]
    private async Task CopyResults()
    {
        var report = GenerateReport();
        try
        {
            if (SetClipboardTextAsync != null)
            {
                await SetClipboardTextAsync(report);
                StatusMessage = "结果已复制到剪贴板";
                StardustMonitorService.Instance.ReportInfo("HealthCheckCopy", "用户复制检测结果");
            }
            else
            {
                StatusMessage = "复制功能未初始化";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"复制失败：{ex.Message}";
        }
    }

    private string GenerateReport()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"GitHub 子域健康检查报告");
        sb.AppendLine($"检测时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"总评：{OverallStatus}");
        sb.AppendLine();
        sb.AppendLine($"┌─────────────┬─────────────────────────────┬──────────┬───────────┐");
        sb.AppendLine($"│ 名称        │ 子域                       │ 状态     │ 延迟      │");
        sb.AppendLine($"├─────────────┼─────────────────────────────┼──────────┼───────────┤");
        foreach (var item in Items)
        {
            var status = item.Status switch
            {
                "OK" => "✅ 正常",
                "SLOW" => "⚠️ 较慢",
                "TIMEOUT" => "⏱ 超时",
                "FAIL" => "❌ 失败",
                _ => "⏳ 未测"
            };
            var latency = item.LatencyMs < 0 ? "--" : $"{item.LatencyMs}ms";
            sb.AppendLine($"│ {item.Name.PadRight(10)} │ {item.Host.PadRight(29)} │ {status.PadRight(8)} │ {latency.PadRight(9)} │");
            if (!string.IsNullOrEmpty(item.ErrorMessage))
            {
                sb.AppendLine($"│             │ 错误：{item.ErrorMessage}");
            }
        }
        sb.AppendLine($"└─────────────┴─────────────────────────────┴──────────┴───────────┘");
        sb.AppendLine();
        sb.AppendLine($"正常：{OkCount} | 较慢：{SlowCount} | 失败：{FailCount}");
        sb.AppendLine();
        sb.AppendLine($"建议：{StatusMessage}");
        return sb.ToString();
    }

    /// <summary>
    /// 只请求取消；IsRunning 由正在执行的 RunCheck 在真正停止后复位，
    /// 避免"按钮恢复可点但后台仍在探测/回写"。
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        if (_cts == null || !IsRunning) return;
        _cts.Cancel();
        StatusMessage = "正在取消：已停止发起新的站点请求…";
    }

    private void UpdateSummary()
    {
        OkCount = Items.Count(x => x.Status == "OK");
        SlowCount = Items.Count(x => x.Status == "SLOW");
        FailCount = Items.Count(x => x.Status == "TIMEOUT" || x.Status == "FAIL");
    }
}
