using System;
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
        IsRunning = true;
        StatusMessage = "正在并行探测全部子域...";
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            var results = await _service.ProbeAllAsync(ct);
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
        catch (Exception ex)
        {
            StatusMessage = $"检测失败：{ex.Message}";
            StardustMonitorService.Instance.ReportError("GitHubHealthCheckException", ex.Message, ex);
        }
        finally
        {
            IsRunning = false;
        }
    }

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

    [RelayCommand]
    private void Cancel()
    {
        _cts?.Cancel();
        StatusMessage = "已取消检测";
        IsRunning = false;
    }

    private void UpdateSummary()
    {
        OkCount = Items.Count(x => x.Status == "OK");
        SlowCount = Items.Count(x => x.Status == "SLOW");
        FailCount = Items.Count(x => x.Status == "TIMEOUT" || x.Status == "FAIL");
    }
}
