using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GithubAccelerator.Core.Services;
using GithubAccelerator.UI.Models;

namespace GithubAccelerator.UI.ViewModels;

/// <summary>
/// 「官网访问诊断」面板（M1，只读观测）。
/// <para>
/// 明确定位：只查询 DoH、只做 TCP+TLS 握手测量、只读系统 DNS；
/// 不写 Hosts、不请求管理员权限、不改 DNS/代理/防火墙、不装证书、不常驻轮询、不自动打开浏览器。
/// 本面板的主要产出是结论：解析层是不是瓶颈。
/// </para>
/// </summary>
public partial class OfficialSiteDiagnosticsViewModel : ObservableObject
{
    private readonly OfficialSiteDiagnosticsService _service;
    private bool _hasAttempted;

    public OfficialSiteDiagnosticsViewModel()
        : this(new OfficialSiteDiagnosticsService())
    {
    }

    public OfficialSiteDiagnosticsViewModel(
        OfficialSiteDiagnosticsService service, OfficialSiteHostsGuardViewModel? guard = null)
    {
        _service = service;
        Guard = guard ?? new OfficialSiteHostsGuardViewModel();
        ScopeText = "核心域名：" + string.Join(" / ", OfficialSiteDiagnosticsDefaults.CoreDomains);
    }

    /// <summary>M2 防护版加速：默认关闭，只在用户预览并确认后写 Hosts。</summary>
    public OfficialSiteHostsGuardViewModel Guard { get; }

    [ObservableProperty]
    private ObservableCollection<OfficialSiteDiagnosticsRow> _rows = new();

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _hasResult;

    [ObservableProperty]
    private string _statusMessage = "点击「重新检测」开始只读诊断（不会修改任何系统设置）";

    [ObservableProperty]
    private string _checkedAtText = "尚未检测";

    [ObservableProperty]
    private string _resolutionText = "解析耗时：—";

    [ObservableProperty]
    private string _settingsText = "单次超时 5s · 并发上限 4 · 每 IP 采样 3 次取中位数";

    [ObservableProperty]
    private string _scopeText = string.Empty;

    [ObservableProperty]
    private string _dohSourceText = "DoH 源状态：尚未查询";

    [ObservableProperty]
    private string _directStatusText = "直连状态：尚未检测";

    [ObservableProperty]
    private bool _isDirectUsable;

    [ObservableProperty]
    private string _conclusionTitle = "结论：尚未检测";

    [ObservableProperty]
    private string _conclusionDetail = "检测后会给出“解析层是否瓶颈”的判断；本面板不会自动写入 Hosts。";

    [ObservableProperty]
    private string _conclusionBadge = "未检测";

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private DiagnosticsConclusionKind _conclusionKind;

    public IBrush ConclusionBrush => ConclusionKind switch
    {
        DiagnosticsConclusionKind.OptimizationPossible => new SolidColorBrush(Color.Parse("#4CAF50")),
        DiagnosticsConclusionKind.NoExpectedGain => new SolidColorBrush(Color.Parse("#2196F3")),
        _ => new SolidColorBrush(Color.Parse("#E8A23D"))
    };

    partial void OnConclusionKindChanged(DiagnosticsConclusionKind value)
    {
        OnPropertyChanged(nameof(ConclusionBrush));
    }

    /// <summary>页面首次显示时自动跑一次（缓存命中则直接展示）；之后仅由用户点「重新检测」。</summary>
    public async Task EnsureLoadedAsync()
    {
        if (_hasAttempted || IsRunning) return;
        await Guard.EnsureLoadedAsync();
        await RunCoreAsync(forceRefresh: false);
    }

    [RelayCommand]
    private async Task Refresh()
    {
        await RunCoreAsync(forceRefresh: true);
    }

    private async Task RunCoreAsync(bool forceRefresh)
    {
        if (IsRunning) return;

        _hasAttempted = true;
        IsRunning = true;
        ProgressText = string.Empty;
        StatusMessage = "正在查询 DoH 并对候选 IP 做 TCP 443 + TLS 握手（只读）…";

        try
        {
            var progress = new Progress<DiagnosticsProgress>(p =>
            {
                ProgressText = $"已完成 {p.CompletedIpCount} / 共 {p.TotalIpCount} 个候选/基线 IP";
            });

            var report = await _service.RunAsync(forceRefresh, progress);
            ApplyReport(report);
            StatusMessage = $"检测完成（只读）：未写入 Hosts、未修改 DNS/代理/防火墙（{report.CheckedAt:HH:mm:ss}）";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "检测已取消";
        }
        catch (Exception ex)
        {
            StatusMessage = $"检测失败：{ex.Message}";
        }
        finally
        {
            IsRunning = false;
            HasResult = Rows.Count > 0;
            ProgressText = string.Empty;
        }
    }

    private void ApplyReport(OfficialSiteDiagnosticsReport report)
    {
        CheckedAtText = report.CheckedAt.ToString("yyyy-MM-dd HH:mm:ss");
        ResolutionText = $"解析耗时：{report.ResolutionElapsedMs} ms（DoH 并行查询）";
        SettingsText = $"单次超时 {report.TimeoutMs / 1000}s · 并发上限 {report.MaxConcurrency} · 每 IP 采样 {report.SampleCount} 次取中位数";
        DirectStatusText = report.DirectUsableText;
        IsDirectUsable = report.DirectUsable;
        DohSourceText = BuildDohSourceText(report);
        ConclusionTitle = report.ConclusionTitle;
        ConclusionDetail = report.ConclusionDetail;
        ConclusionKind = report.ConclusionKind;
        ConclusionBadge = report.ConclusionKind switch
        {
            DiagnosticsConclusionKind.OptimizationPossible => "有优化空间",
            DiagnosticsConclusionKind.NoExpectedGain => "换 IP 改善不了",
            _ => "无法判断"
        };

        Rows.Clear();
        foreach (var domain in report.Domains)
        {
            foreach (var row in domain.Rows)
            {
                Rows.Add(new OfficialSiteDiagnosticsRow(row));
            }
        }

        HasResult = Rows.Count > 0;
    }

    /// <summary>逐源展示 DoH 成功/失败原因，失败必须可见，不能只显示“不可用”。</summary>
    private static string BuildDohSourceText(OfficialSiteDiagnosticsReport report)
    {
        var sourceNames = report.Domains
            .SelectMany(d => d.DohResults)
            .Select(r => r.SourceName)
            .Distinct()
            .ToList();

        if (sourceNames.Count == 0) return "DoH 源状态：本次未执行查询";

        var parts = sourceNames.Select(name =>
        {
            var results = report.Domains
                .SelectMany(d => d.DohResults)
                .Where(r => r.SourceName == name)
                .ToList();
            var ok = results.Count(r => r.Success);
            var total = results.Count;
            if (ok == total)
            {
                var slowest = results.Max(r => r.ElapsedMs);
                return $"{name}：{ok}/{total} 成功（最慢 {slowest}ms）";
            }

            var failures = results
                .Where(r => !r.Success)
                .Select(r => $"{r.Domain} {r.FailureReason}")
                .Take(2);
            return $"{name}：{ok}/{total} 成功（{string.Join("；", failures)}）";
        });

        return "DoH 源状态：" + string.Join(" ｜ ", parts);
    }
}
