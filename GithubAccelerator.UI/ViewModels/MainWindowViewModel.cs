using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GithubAccelerator.Core.Services;
using GithubAccelerator.Models;
using GithubAccelerator.Services;
using GithubAccelerator.UI.Controls;
using GithubAccelerator.UI.Services;
using GithubAccelerator.UI.Views;
using Microsoft.Extensions.Logging;
using NewLife.Log;
using Serilog;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace GithubAccelerator.UI.ViewModels;

/// <summary>
/// Hosts 视图中单行数据的展示模型，包含行号、文本与匹配高亮状态。
/// </summary>
public partial class HostsLineViewModel : ObservableObject
{
    private static readonly Avalonia.Media.SolidColorBrush _matchBrush =
        new(Avalonia.Media.Color.Parse("#FFF59D"));
    private static readonly Avalonia.Media.SolidColorBrush _defaultBrush =
        new(Avalonia.Media.Color.FromArgb(0, 255, 255, 255));

    [ObservableProperty]
    private int _lineNumber;

    [ObservableProperty]
    private string _text = string.Empty;

    [ObservableProperty]
    private bool _isMatch;

    /// <summary>
    /// 行号文本（右对齐 4 位），便于在 UI 中显示统一的列宽。
    /// </summary>
    public string LineNumberText => LineNumber.ToString("D4");

    /// <summary>
    /// 匹配时返回高亮背景画刷，否则返回透明画刷。
    /// </summary>
    public Avalonia.Media.IBrush MatchBackground => IsMatch ? _matchBrush : _defaultBrush;

    partial void OnLineNumberChanged(int value) => OnPropertyChanged(nameof(LineNumberText));
    partial void OnIsMatchChanged(bool value) => OnPropertyChanged(nameof(MatchBackground));
}

public partial class SourceStatusViewModel : ObservableObject
{
    private readonly string _sourceUrl = string.Empty;
    private static readonly HttpClient _httpClient = new();

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _url = string.Empty;

    [ObservableProperty]
    private long _responseTime;

    [ObservableProperty]
    private double _score;

    [ObservableProperty]
    private bool _isHealthy;

    [ObservableProperty]
    private DateTime _lastTestTime;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isDnsProbe;

    /// <summary>
    /// 同源镜像 URL 列表（与 Url 内容相同，应用时主 URL 失败会依次尝试）
    /// </summary>
    public string[] MirrorUrls { get; set; } = Array.Empty<string>();

    public event Action<SourceStatusViewModel>? OnApplyRequested;

    public string ResponseTimeText => ResponseTime > 0 ? $"{ResponseTime} ms" : "超时";

    public string ScoreText => Score > 0 ? $"{Score:F1}" : "-";

    public string LastTestTimeText => LastTestTime != default ? LastTestTime.ToString("HH:mm:ss") : "-";

    public Avalonia.Media.Color StatusColor => IsHealthy ? Avalonia.Media.Color.Parse("#4CAF50") : Avalonia.Media.Color.Parse("#FF5722");

    public Avalonia.Media.Color ResponseTimeColor => ResponseTime switch
    {
        0 => Avalonia.Media.Color.Parse("#FF5722"),
        < 300 => Avalonia.Media.Color.Parse("#4CAF50"),
        < 800 => Avalonia.Media.Color.Parse("#FFC107"),
        _ => Avalonia.Media.Color.Parse("#FF5722")
    };

    public Avalonia.Media.Color ScoreColor => Score switch
    {
        0 => Avalonia.Media.Color.Parse("#FF5722"),
        >= 80 => Avalonia.Media.Color.Parse("#4CAF50"),
        >= 50 => Avalonia.Media.Color.Parse("#FFC107"),
        _ => Avalonia.Media.Color.Parse("#FF5722")
    };

    [RelayCommand]
    private void ApplySingleSource()
    {
        OnApplyRequested?.Invoke(this);
    }

    [RelayCommand]
    private async Task PreviewHosts()
    {
        var topLevel = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime lifetime
            ? lifetime.MainWindow
            : null;
        if (topLevel == null) return;

        if (IsDnsProbe)
        {
            // DNS 探测源：优先使用缓存，秒开预览
            var cached = ChinazDnsService.CachedContent;
            if (cached != null)
            {
                await SourcePreviewWindow.ShowAsync(
                    topLevel,
                    $"📄 预览 - {Name}",
                    $"多源 DNS 探测生成的 Hosts 数据（缓存于 {ChinazDnsService.CacheAgeDescription}）",
                    cached);
                return;
            }

            // 无缓存时才显示加载中
            await SourcePreviewWindow.ShowLoadingAsync(
                topLevel,
                $"📄 预览 - {Name}",
                "正在通过多源 DNS 查询 GitHub IP...",
                async () =>
                {
                    var chinaz = ChinazDnsService.Instance;
                    return await chinaz.GenerateHostsContentAsync();
                });
        }
        else
        {
            // 普通远程源：直接加载
            await SourcePreviewWindow.ShowLoadingAsync(
                topLevel,
                $"📄 预览 - {Name}",
                $"远程数据源 Hosts 内容",
                async () =>
                {
                    var response = await _httpClient.GetStringAsync(Url);
                    return response;
                });
        }
    }

    public void UpdateFromMetrics(SourcePerformanceMetrics metrics)
    {
        Name = metrics.Name;
        Url = metrics.Url;
        ResponseTime = (long)metrics.AverageResponseTimeMs;
        Score = metrics.OverallScore;
        IsHealthy = metrics.SuccessRate > 0.5;
        LastTestTime = metrics.LastTestTime;
        IsDnsProbe = metrics.IsDnsProbe;
        OnPropertyChanged(nameof(ResponseTimeText));
        OnPropertyChanged(nameof(ScoreText));
        OnPropertyChanged(nameof(LastTestTimeText));
        OnPropertyChanged(nameof(StatusColor));
        OnPropertyChanged(nameof(ResponseTimeColor));
        OnPropertyChanged(nameof(ScoreColor));
    }
}

public partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly ISourcePerformanceMonitor _performanceMonitor;
    private readonly GithubHostsService _hostsService;
    private readonly GithubConnectionService _connectionService;
    private readonly IHostsFileService _hostsFileService;
    private readonly IHostsBackupService _backupService;
    private readonly IDnsFlusher _dnsFlusher;
    private readonly DownloadAccelerationService _downloadAccelerationService;
    private readonly SettingsViewModel _settings;
    private readonly SourceStatisticsService _statsService;
    private System.Timers.Timer? _updateTimer;
    private System.Timers.Timer? _githubConnectivityTimer;
    private System.Timers.Timer? _periodicHostsRefreshTimer;

    [ObservableProperty]
    private bool _isGitHubAccessible;

    [ObservableProperty]
    private string _gitHubStatusText = "直连：未测试";

    [ObservableProperty]
    private ObservableCollection<SourceStatusViewModel> _sources = new();

    [ObservableProperty]
    private bool _isMonitoring;

    [ObservableProperty]
    private bool _isAdmin;

    [ObservableProperty]
    private string _bestSourceName = string.Empty;

    [ObservableProperty]
    private long _bestSourceResponseTime;

    [ObservableProperty]
    private double _bestSourceScore;

    [ObservableProperty]
    private string _statusMessage = "就绪";

    [ObservableProperty]
    private bool _isHostsApplied;

    [ObservableProperty]
    private string _currentHostsSource = string.Empty;

    [ObservableProperty]
    private Control? _currentView;

    [ObservableProperty]
    private bool _isDashboardVisible = true;

    [ObservableProperty]
    private bool _isSettingsVisible;

    [ObservableProperty]
    private bool _isLogViewerVisible;

    [ObservableProperty]
    private LogViewerViewModel? _logViewer;

    [ObservableProperty]
    private BackupManagerViewModel? _backupManager;

    [ObservableProperty]
    private HostsGroupViewModel? _hostsGroupManager;

    [ObservableProperty]
    private bool _isHostsGroupVisible;

    [ObservableProperty]
    private NetworkMonitorViewModel? _networkMonitor;

    [ObservableProperty]
    private bool _isNetworkMonitorVisible;

    [ObservableProperty]
    private DownloadAcceleratorViewModel? _downloadAccelerator;

    [ObservableProperty]
    private bool _isDownloadAcceleratorVisible;

    [ObservableProperty]
    private HealthCheckViewModel? _healthCheck;

    [ObservableProperty]
    private bool _isHealthCheckVisible;

    [ObservableProperty]
    private OfficialSiteDiagnosticsViewModel? _officialSiteDiagnostics;

    [ObservableProperty]
    private bool _isOfficialSiteDiagnosticsVisible;

    [ObservableProperty]
    private CheckAndRepairViewModel? _checkAndRepair;

    [ObservableProperty]
    private HelpViewModel? _help;

    [ObservableProperty]
    private RestoreOriginalStateViewModel? _restoreOriginalState;

    [ObservableProperty]
    private bool _isCheckAndRepairSelected;

    [ObservableProperty]
    private bool _isHostsAndBackupSelected;

    [ObservableProperty]
    private bool _isHelpSelected;

    [ObservableProperty]
    private string _hostsContent = string.Empty;

    /// <summary>
    /// Hosts 视图的搜索关键词（匹配行内容时高亮显示）。
    /// </summary>
    [ObservableProperty]
    private string _hostsSearchText = string.Empty;

    /// <summary>
    /// Hosts 视图显示的行集合（已包含行号，按 <see cref="HostsSearchText"/> 过滤）。
    /// </summary>
    public ObservableCollection<HostsLineViewModel> HostsLines { get; } = new();

    partial void OnHostsContentChanged(string value) => RebuildHostsLines();
    partial void OnHostsSearchTextChanged(string value) => RebuildHostsLines();

    private void RebuildHostsLines()
    {
        HostsLines.Clear();
        if (string.IsNullOrEmpty(HostsContent))
        {
            OnPropertyChanged(nameof(HasHostsLines));
            OnPropertyChanged(nameof(HasNoHostsLines));
            return;
        }

        var keyword = HostsSearchText?.Trim() ?? string.Empty;
        var lines = HostsContent.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (!string.IsNullOrEmpty(keyword) &&
                !line.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            HostsLines.Add(new HostsLineViewModel
            {
                LineNumber = i + 1,
                Text = line,
                IsMatch = !string.IsNullOrEmpty(keyword) &&
                          line.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            });
        }
        OnPropertyChanged(nameof(HasHostsLines));
        OnPropertyChanged(nameof(HasNoHostsLines));
    }

    public bool HasHostsLines => HostsLines.Count > 0;
    public bool HasNoHostsLines => HostsLines.Count == 0;

    [ObservableProperty]
    private string _preApplyLatency = "-- ms";

    [ObservableProperty]
    private string _postApplyLatency = "-- ms";

    [ObservableProperty]
    private string _latencyImprovement = "--";

    [ObservableProperty]
    private Avalonia.Media.Color _improvementColor = Avalonia.Media.Color.Parse("#FFFFFF");

    [ObservableProperty]
    private bool _isDarkMode;

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private string _currentPageTitle = "仪表盘";

    [ObservableProperty]
    private bool _isDashboardSelected = true;

    [ObservableProperty]
    private bool _isNetworkMonitorSelected;

    [ObservableProperty]
    private bool _isDownloadAcceleratorSelected;

    [ObservableProperty]
    private bool _isHealthCheckSelected;

    [ObservableProperty]
    private bool _isOfficialSiteDiagnosticsSelected;

    [ObservableProperty]
    private bool _isHostsContentSelected;

    [ObservableProperty]
    private bool _isHostsGroupSelected;

    [ObservableProperty]
    private bool _isBackupSelected;

    [ObservableProperty]
    private bool _isSettingsSelected;

    [ObservableProperty]
    private bool _isLogViewerSelected;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private int _sortOption = 0;

    [ObservableProperty]
    private string _appVersion = string.Empty;

    private readonly AccelerationStateMachine _accelState = new();
    private CancellationTokenSource? _applyCts;
    private string? _applyIssue;
    private ElevationResult? _lastElevationResult;

    /// <summary>进行中（可取消）：由状态机派生，避免"按钮灰着但后台在跑"。</summary>
    public bool IsApplyingHosts => _accelState.IsBusy;

    /// <summary>唯一的可观测状态文案。</summary>
    public string AccelerationStateText => _accelState.StateText;

    public bool CanCancelApply => _accelState.CanCancel;

    private void RaiseAccelerationStateChanged()
    {
        OnPropertyChanged(nameof(IsApplyingHosts));
        OnPropertyChanged(nameof(AccelerationStateText));
        OnPropertyChanged(nameof(CanCancelApply));
        ApplyHostsCommand.NotifyCanExecuteChanged();
        ClearAppliedHostsCommand.NotifyCanExecuteChanged();
        RunSpeedTestCommand.NotifyCanExecuteChanged();
        CancelApplyCommand.NotifyCanExecuteChanged();
    }

    /// <summary>进入进行中；已在运行时返回 false，保证同一时刻只有一个流程。</summary>
    private bool BeginAcceleration()
    {
        if (_accelState.State == AccelerationState.WaitingForElevation)
        {
            StatusMessage = "正在等待提权确认，请先处理 UAC 提示";
            return false;
        }

        if (!_accelState.Begin())
        {
            StatusMessage = "已有加速任务在执行中";
            return false;
        }

        _applyCts?.Dispose();
        _applyCts = new CancellationTokenSource();
        _applyIssue = null;
        AccelerationProgressText = "已完成 0 项，准备开始…";
        HasAllSourcesFailure = false;
        AllSourcesFailureText = string.Empty;
        RaiseAccelerationStateChanged();
        StatusMessage = "开始加速…";
        return true;
    }

    /// <summary>结束进行中：按 Hosts 实际是否写入决定"已加速/部分失败/回到原状态"。</summary>
    private void FinishAcceleration()
    {
        if (!_accelState.IsBusy) return;

        if (IsHostsApplied)
        {
            var issue = _applyIssue;
            if (string.IsNullOrEmpty(issue)) _accelState.Complete();
            else _accelState.CompleteWithIssues(issue);
        }
        else
        {
            _accelState.FailedBeforeWrite(_applyIssue);
        }

        _applyIssue = null;
        _applyCts?.Dispose();
        _applyCts = null;
        AccelerationProgressText = string.Empty;
        RaiseAccelerationStateChanged();
    }

    private bool IsCancelRequested => _applyCts?.IsCancellationRequested == true;

    /// <summary>设置确定进度：只报已完成的项数，不猜百分比。</summary>
    private void SetAccelerationProgress(int done, int total, string phase)
    {
        AccelerationProgressText = $"已完成 {done}/共 {total} 项（{phase}）";
    }

    /// <summary>取消当前进行中的任务：不会写入 Hosts，回到上一个稳定状态。</summary>
    [RelayCommand(CanExecute = nameof(CanCancelApply))]
    private void CancelApply()
    {
        if (!_accelState.IsBusy) return;
        _applyCts?.Cancel();
        StatusMessage = "正在取消（不会写入 Hosts）…";
        Log.Information("[State] 用户请求取消加速");
    }

    private void AbortIfCanceled()
    {
        if (!IsCancelRequested) return;

        _accelState.Cancel();
        _applyIssue = null;
        AccelerationProgressText = string.Empty;
        RaiseAccelerationStateChanged();
        StatusMessage = "已取消：Hosts 未被修改";
        Log.Information("[State] 加速已取消，Hosts 未修改");
    }

    public ObservableCollection<SourceStatusViewModel> FilteredSources { get; } = new();

    public ObservableCollection<ToastItem> Toasts => ToastService.Instance.Toasts;

    [ObservableProperty]
    private ObservableCollection<OperationRecord> _operationHistory = new();

    [ObservableProperty]
    private bool _hasNotifications;

    [ObservableProperty]
    private string _latestNotification = string.Empty;

    /// <summary>失败自救提示：一句人话原因 + 一个按钮；成功或用户关闭后清空。</summary>
    [ObservableProperty]
    private FailureAdvice? _failureAdvice;

    /// <summary>进行中任务的确定进度：已完成 X/共 N 项（不编造百分比）。</summary>
    [ObservableProperty]
    private string _accelerationProgressText = string.Empty;

    /// <summary>全部数据源失败详情：检测时间 + 每个源的原因 + 直连动作。</summary>
    [ObservableProperty]
    private bool _hasAllSourcesFailure = false;

    [ObservableProperty]
    private string _allSourcesFailureText = string.Empty;

    public bool HasFailureAdvice => FailureAdvice != null;
    public string FailureCauseText => FailureAdvice?.Cause ?? string.Empty;
    public string FailureActionText => FailureAdvice?.ActionText ?? string.Empty;

    partial void OnFailureAdviceChanged(FailureAdvice? value)
    {
        OnPropertyChanged(nameof(HasFailureAdvice));
        OnPropertyChanged(nameof(FailureCauseText));
        OnPropertyChanged(nameof(FailureActionText));
    }

    private void ShowFailureAdvice(FailureKind kind)
    {
        FailureAdvice = FailureAdvisor.Get(kind);
    }

    [RelayCommand]
    private void DismissFailureAdvice()
    {
        FailureAdvice = null;
    }

    /// <summary>
    /// 一键改用原始链接：已应用加速时移除本程序的 Hosts 块；未应用时说明当前已是直连。
    /// 不猜测、不假装"仍在加速"。
    /// </summary>
    [RelayCommand]
    private async Task UseDirectConnection()
    {
        if (!IsHostsApplied)
        {
            StatusMessage = "当前未应用加速，已经是原始链接直连，无需改动";
            HasAllSourcesFailure = false;
            AllSourcesFailureText = string.Empty;
            return;
        }

        await ClearAppliedHosts();
        HasAllSourcesFailure = false;
        AllSourcesFailureText = string.Empty;
        StatusMessage = "已恢复原始访问（已移除本程序的 Hosts 加速块）";
    }

    [RelayCommand]
    private void DismissAllSourcesFailure()
    {
        HasAllSourcesFailure = false;
        AllSourcesFailureText = string.Empty;
    }

    /// <summary>执行失败提示里的那一个按钮，不额外弹窗、不扩大操作范围。</summary>
    [RelayCommand]
    private async Task RunFailureAdviceAction()
    {
        var advice = FailureAdvice;
        if (advice == null) return;
        FailureAdvice = null;

        switch (advice.Kind)
        {
            case FailureKind.ElevationDenied:
            case FailureKind.HostsWriteDenied:
                await RequestElevationAsync(LaunchIntent.ApplyHosts);
                break;
            case FailureKind.AllMirrorsUnreachable:
                await UseDirectConnection();
                break;
            case FailureKind.NoNetwork:
                await TestGitHubConnectionAsync(showNotification: true);
                break;
            case FailureKind.HostsRewrittenByThirdParty:
                ShowHostsAndBackup();
                break;
            case FailureKind.GitConfigConflict:
                ShowSettings();
                break;
        }
    }

    public bool HasSelectedSources => Sources.Any(s => s.IsSelected);

    public int SelectedSourcesCount => Sources.Count(s => s.IsSelected);

    public int TotalSourcesCount => Sources.Count;

    public int HealthySourcesCount => Sources.Count(s => s.IsHealthy);

    /// <summary>刷新进行中：计数类指标一律显示“—”，不用 0 冒充“没有数据源”。</summary>
    [ObservableProperty]
    private bool _isRefreshingSources;

    public string TotalSourcesText => FormatSourceCountText(IsRefreshingSources, Sources.Count);

    public string HealthySourcesText => FormatSourceCountText(IsRefreshingSources, Sources.Count(s => s.IsHealthy));

    public string SourcesCountText => IsRefreshingSources ? "—" : $"共 {Sources.Count} 条";

    /// <summary>仪表盘计数口径：刷新中一律“—”，避免 0 与“已刷新 N 个”自相矛盾。</summary>
    public static string FormatSourceCountText(bool refreshing, int count) => refreshing ? "—" : count.ToString();

    /// <summary>
    /// 推荐卡唯一来源：从同一个 Sources 集合里挑“健康且评分 &gt; 0”的最高分项；
    /// 没有就返回 null（卡片显示“—”），不允许出现“列表 0 条却有推荐分”的矛盾。
    /// </summary>
    public static SourceStatusViewModel? SelectRecommendedSource(IEnumerable<SourceStatusViewModel> sources)
        => sources.Where(s => s.IsHealthy && s.Score > 0)
                  .OrderByDescending(s => s.Score)
                  .FirstOrDefault();

    public string BestSourceResponseText => string.IsNullOrEmpty(BestSourceName) ? "—" : $"{BestSourceResponseTime} ms";

    public string CurrentBestSourceText => string.IsNullOrEmpty(BestSourceName) ? "尚未探测完成" : BestSourceName;

    /// <summary>第一层「能不能用」的唯一结论：不推断、不夸大。</summary>
    public string AvailabilityConclusion => IsHostsApplied
        ? "GitHub 当前走本机加速（Hosts 已写入）"
        : "GitHub 当前走原始直连（未写入加速）";

    public string AvailabilityDetail => IsHostsApplied
        ? "可随时恢复原始访问。push、登录、issue 仍然直连 GitHub，不经过加速块。"
        : "能否打开取决于本机网络。开启前会先比较候选与直连：候选没有超过直连 20% 时不写入 Hosts。";

    partial void OnIsRefreshingSourcesChanged(bool value)
    {
        OnPropertyChanged(nameof(TotalSourcesText));
        OnPropertyChanged(nameof(HealthySourcesText));
        OnPropertyChanged(nameof(SourcesCountText));
    }

    partial void OnBestSourceNameChanged(string value)
    {
        OnPropertyChanged(nameof(BestSourceResponseText));
        OnPropertyChanged(nameof(CurrentBestSourceText));
    }

    partial void OnBestSourceResponseTimeChanged(long value) => OnPropertyChanged(nameof(BestSourceResponseText));

    public bool HasFilteredSources => FilteredSources.Count > 0;
    private void CheckAdminPrivileges()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        IsAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);
        if (!IsAdmin)
        {
            Log.Warning("应用程序未以管理员身份运行，部分功能可能受限");
        }
        else
        {
            Log.Information("应用程序以管理员身份运行");
        }
    }

    private readonly OperationHistoryService _historyService = OperationHistoryService.Instance;
    private readonly NotificationService _notificationService = NotificationService.Instance;

    public bool MinimizeToTray => _settings.MinimizeToTray;

    /// <summary>主窗口读取托盘类设置的入口，保证"改了立即生效"。</summary>
    internal SettingsViewModel Settings => _settings;

    public MainWindowViewModel()
    {
        Log.Information("MainWindowViewModel 初始化开始");

        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        AppVersion = $"v{version?.Major ?? 1}.{version?.Minor ?? 0}.{version?.Build ?? 0}";

        CheckAdminPrivileges();
        
        var startupManager = PlatformServiceFactory.CreateStartupManager();
        _settings = SettingsViewModel.Create(startupManager);
        _statsService = SourceStatisticsService.Instance;

        var httpClient = new HttpClient();
        var logger = new SerilogLoggerAdapter<SourcePerformanceMonitor>();
        _performanceMonitor = new SourcePerformanceMonitor(httpClient, logger);
        _hostsService = new GithubHostsService();
        _connectionService = new GithubConnectionService();
        _hostsFileService = new WindowsHostsFileService();
        _backupService = new HostsBackupService(_hostsFileService);
        _dnsFlusher = PlatformServiceFactory.CreateDnsFlusher();
        _downloadAccelerationService = new DownloadAccelerationService();

        _historyService.OnOperationRecorded += OnOperationRecorded;
        _notificationService.OnNotification += OnNotificationReceived;

        Log.Information("正在预初始化网络监控");
        NetworkMonitor = new NetworkMonitorViewModel();
        // 诊断/防护版状态供「检查与修复」与「回到原始状态」共用，构造无网络访问。
        OfficialSiteDiagnostics = new OfficialSiteDiagnosticsViewModel();

        Log.Information("正在加载仪表盘");
        ShowDashboard();

        // 先订阅集合变化，确保初始化期间加入的数据源也能触发计数刷新
        Sources.CollectionChanged += OnSourcesCollectionChanged;

        Log.Information("正在初始化数据源");
        InitializeSourcesFromConfig();
        ApplySettings();
        CheckHostsStatus();

        // 自动启动监控 - 始终检测所有数据源状态
        Log.Information("启动性能监控");
        _performanceMonitor.StartMonitoring();
        IsMonitoring = true;

        // 定时更新间隔直接取设置值（界面下限为 30 秒）；改设置后立即重设间隔。
        var updateInterval = Math.Max(30, _settings.TestInterval) * 1000;
        _updateTimer = new System.Timers.Timer(updateInterval);
        _updateTimer.Elapsed += async (s, e) => await AutoUpdateAsync();
        _updateTimer.Start();
        _settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.TestInterval) && _updateTimer != null)
            {
                _updateTimer.Interval = Math.Max(30, _settings.TestInterval) * 1000;
            }
        };

        // 启动 GitHub 连通性定时检查（10 分钟一次）
        _githubConnectivityTimer = new System.Timers.Timer(600000);
        _githubConnectivityTimer.Elapsed += async (s, e) => await TestGitHubConnectionAsync();
        _githubConnectivityTimer.Start();

        // 立即测试 GitHub 连接
        Log.Information("启动时立即测试 GitHub 连接");
        _ = TestGitHubConnectionAsync();

        // 立即刷新数据源状态
        Log.Information("启动时立即刷新数据源状态");
        _ = RefreshSources();

        // 启动后台定时 Hosts 优化（每30分钟静默刷新）
        StartPeriodicHostsRefresh();

        StatusMessage = "已启动自动检测，正在测试所有数据源...";

        IsDarkMode = ThemeManager.IsDarkMode;

        // 首次启动时显示单页引导（双击 + 一次点击即可用）
        _ = ShowFirstRunGuideOnFirstLaunchAsync();

        // 启动时检查是否需要自动应用Hosts
        if (_settings.AutoApplyHosts)
        {
            _ = Task.Run(async () =>
            {
                // 等待3秒让连接测试和性能监控收集数据
                await Task.Delay(3000);
                await AutoApplyHostsOnStartup();
            });
        }

        foreach (var record in _historyService.GetRecentRecords(20))
        {
            OperationHistory.Add(record);
        }

        PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(SearchText) || e.PropertyName == nameof(SortOption))
            {
                UpdateFilteredSources();
            }
        };

        // 监听 Sources 集合变化，自动更新数据源卡片计数
        // （订阅已提前到初始化之前，见构造函数前半段）

        Log.Information("MainWindowViewModel 初始化完成");
    }

    private void OnSourcesCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(TotalSourcesCount));
        OnPropertyChanged(nameof(HealthySourcesCount));
        OnPropertyChanged(nameof(HasFilteredSources));
        OnPropertyChanged(nameof(TotalSourcesText));
        OnPropertyChanged(nameof(HealthySourcesText));
        OnPropertyChanged(nameof(SourcesCountText));

        if (e.NewItems != null)
        {
            foreach (SourceStatusViewModel item in e.NewItems)
            {
                item.PropertyChanged -= OnSourceItemPropertyChanged;
                item.PropertyChanged += OnSourceItemPropertyChanged;
            }
        }
        if (e.OldItems != null)
        {
            foreach (SourceStatusViewModel item in e.OldItems)
            {
                item.PropertyChanged -= OnSourceItemPropertyChanged;
            }
        }
    }

    private void OnSourceItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SourceStatusViewModel.IsHealthy) ||
            e.PropertyName == nameof(SourceStatusViewModel.IsSelected) ||
            e.PropertyName == nameof(SourceStatusViewModel.Score) ||
            e.PropertyName == nameof(SourceStatusViewModel.ResponseTime))
        {
            OnPropertyChanged(nameof(HealthySourcesCount));
            OnPropertyChanged(nameof(HealthySourcesText));
            OnPropertyChanged(nameof(HasSelectedSources));
            OnPropertyChanged(nameof(SelectedSourcesCount));

            // 推荐卡也直接取自同一个 Sources 集合，保证列表/计数/推荐三者口径一致
            UpdateBestSource();
        }
    }

    private void UpdateFilteredSources()
    {
        var filtered = Sources.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var search = SearchText.ToLowerInvariant();
            filtered = filtered.Where(s => 
                s.Name.ToLowerInvariant().Contains(search) || 
                s.Url.ToLowerInvariant().Contains(search));
        }

        filtered = SortOption switch
        {
            1 => filtered.OrderBy(s => s.ResponseTime),
            2 => filtered.OrderByDescending(s => s.ResponseTime),
            3 => filtered.OrderBy(s => s.Score),
            4 => filtered.OrderByDescending(s => s.Score),
            5 => filtered.OrderBy(s => s.Name),
            _ => filtered.OrderByDescending(s => s.Score)
        };

        FilteredSources.Clear();
        foreach (var source in filtered)
        {
            FilteredSources.Add(source);
        }
        OnPropertyChanged(nameof(HasFilteredSources));
    }

    private void OnOperationRecorded(OperationRecord record)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            OperationHistory.Insert(0, record);
            if (OperationHistory.Count > 50)
            {
                OperationHistory.RemoveAt(OperationHistory.Count - 1);
            }
        });
    }

    private void OnNotificationReceived(NotificationMessage notification)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            LatestNotification = $"{notification.TypeIcon} {notification.Title}: {notification.Message}";
            HasNotifications = true;
        });
    }

    [RelayCommand]
    private void DismissNotification()
    {
        HasNotifications = false;
        LatestNotification = string.Empty;
    }

    [RelayCommand]
    private void DismissToast(ToastItem toast)
    {
        ToastService.Instance.Remove(toast);
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        ThemeManager.ToggleTheme();
        IsDarkMode = ThemeManager.IsDarkMode;
        var themeName = IsDarkMode ? "暗色" : "亮色";
        StatusMessage = $"已切换到{themeName}主题";
        _historyService.Record(OperationType.ThemeChanged, $"切换到{themeName}主题");
    }

    [RelayCommand]
    private void ShowDashboard()
    {
        CurrentView = new DashboardView { DataContext = this };
        CurrentPageTitle = "仪表盘";
        ResetNavigationSelection();
        IsDashboardSelected = true;
        IsDashboardVisible = true;
        IsSettingsVisible = false;
    }

    [RelayCommand]
    private void ShowSettings()
    {
        var settingsView = new SettingsView();
        settingsView.DataContext = _settings;
        CurrentView = settingsView;
        CurrentPageTitle = "设置";
        ResetNavigationSelection();
        IsSettingsSelected = true;
        IsDashboardVisible = false;
        IsSettingsVisible = true;
        IsLogViewerVisible = false;
    }

    /// <summary>「检查与修复」复用三个既有排查面板：依赖站点检查 + 官网诊断 + 连续采样。</summary>
    private void EnsureCheckAndRepairViewModels()
    {
        HealthCheck ??= new HealthCheckViewModel();
        OfficialSiteDiagnostics ??= new OfficialSiteDiagnosticsViewModel();
        NetworkMonitor ??= new NetworkMonitorViewModel();
    }

    /// <summary>「Hosts 与备份」复用 Hosts 内容 / 分组 / 备份 / 回到原始状态。</summary>
    private void EnsureHostsAndBackupViewModels()
    {
        EnsureCheckAndRepairViewModels();
        HostsGroupManager ??= new HostsGroupViewModel();
        BackupManager ??= new BackupManagerViewModel();
        RestoreOriginalState ??= new RestoreOriginalStateViewModel(
            this, _settings, OfficialSiteDiagnostics!.Guard, BackupManager);
    }

    private void EnsureHelpViewModel()
    {
        LogViewer ??= new LogViewerViewModel();
        Help ??= new HelpViewModel(this, _settings, LogViewer);
    }

    [RelayCommand]
    private void ShowCheckAndRepair()
    {
        EnsureCheckAndRepairViewModels();
        CheckAndRepair ??= new CheckAndRepairViewModel(
            HealthCheck!, OfficialSiteDiagnostics!, NetworkMonitor!);

        var view = new GithubAccelerator.UI.Views.CheckAndRepairView();
        view.DataContext = CheckAndRepair;
        CurrentView = view;
        CurrentPageTitle = "检查与修复";
        ResetNavigationSelection();
        IsCheckAndRepairSelected = true;
        IsDashboardVisible = false;
        IsSettingsVisible = false;
    }

    [RelayCommand]
    private void ShowDownloadAccelerator()
    {
        DownloadAccelerator ??= new DownloadAcceleratorViewModel();

        var view = new GithubAccelerator.UI.Views.DownloadAcceleratorView();
        view.DataContext = DownloadAccelerator;
        CurrentView = view;
        CurrentPageTitle = "下载加速器";
        ResetNavigationSelection();
        IsDownloadAcceleratorSelected = true;
        IsDashboardVisible = false;
        IsSettingsVisible = false;
    }

    [RelayCommand]
    private void ShowHostsAndBackup()
    {
        EnsureHostsAndBackupViewModels();

        var view = new GithubAccelerator.UI.Views.HostsAndBackupView();
        view.DataContext = this;
        CurrentView = view;
        CurrentPageTitle = "Hosts 与备份";
        ResetNavigationSelection();
        IsHostsAndBackupSelected = true;
        IsDashboardVisible = false;
        IsSettingsVisible = false;
        _ = RefreshHostsContent();
        RestoreOriginalState?.RefreshStatus();
    }

    [RelayCommand]
    private void ShowHelp()
    {
        EnsureHelpViewModel();

        var view = new GithubAccelerator.UI.Views.HelpView();
        view.DataContext = Help;
        CurrentView = view;
        CurrentPageTitle = "帮助与支持";
        ResetNavigationSelection();
        IsHelpSelected = true;
        IsDashboardVisible = false;
        IsSettingsVisible = false;
    }

    private void ResetNavigationSelection()
    {
        IsDashboardSelected = false;
        IsNetworkMonitorSelected = false;
        IsHostsContentSelected = false;
        IsHostsGroupSelected = false;
        IsBackupSelected = false;
        IsSettingsSelected = false;
        IsLogViewerSelected = false;
        IsDownloadAcceleratorSelected = false;
        IsHealthCheckSelected = false;
        IsOfficialSiteDiagnosticsSelected = false;
        IsCheckAndRepairSelected = false;
        IsHostsAndBackupSelected = false;
        IsHelpSelected = false;
    }

    [RelayCommand]
    private void ShowAbout()
    {
        try
        {
            var window = App.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

            if (window == null) return;

            var aboutWindow = new AboutWindow();
            aboutWindow.ShowDialog(window);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ShowAbout error: {ex}");
        }
    }

    [RelayCommand]
    private async Task ShowQuickGuide()
    {
        try
        {
            await ShowFirstRunGuideAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "显示使用指南失败");
        }
    }

    /// <summary>
    /// 首次启动时自动显示单页引导（延迟 3 秒，等 UI 加载完成）。
    /// 只在从未完成过引导时出现；跳过也算完成，之后可从左侧「帮助与支持」重新进入。
    /// </summary>
    private async Task ShowFirstRunGuideOnFirstLaunchAsync()
    {
        if (_settings.HasCompletedFirstRunGuide) return;
        if (!_settings.ShowQuickGuideOnStartup) return;

        await Task.Delay(3000); // 等 UI 加载完成

        try
        {
            await ShowFirstRunGuideAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "首次启动显示引导失败");
        }
        finally
        {
            // 关闭后清理提权结果，避免影响后续判断
            _lastElevationResult = null;
        }
    }

    /// <summary>
    /// 打开首启引导并记录"已完成"。Started/Skipped 才写入设置；失败则下次启动仍会显示。
    /// </summary>
    private async Task ShowFirstRunGuideAsync()
    {
        var window = App.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

        if (window == null) return;

        var viewModel = new FirstRunGuideViewModel(
            ProbeFirstRunNetworkAsync,
            StartAccelerationFromGuideAsync,
            SaveFirstRunGuideOptions);

        // 打开即检测一次网络，用户不用再点第二次
        _ = viewModel.CheckNetworkAsync();

        var result = await FirstRunGuideWindow.ShowAsync(window, viewModel);
        if (result is FirstRunGuideResult.Started or FirstRunGuideResult.Skipped)
        {
            _settings.HasCompletedFirstRunGuide = true;
            SaveSettings();
        }
    }

    /// <summary>首启网络检测：只做可读结论，不把状态码暴露给用户。</summary>
    private async Task<FirstRunNetworkResult> ProbeFirstRunNetworkAsync()
    {
        try
        {
            var (latency, _, success) = await _connectionService.TestGitHubLatencyAsync();
            return success
                ? new FirstRunNetworkResult(true, latency)
                : new FirstRunNetworkResult(false, 0);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "首启网络检测失败");
            return new FirstRunNetworkResult(false, 0);
        }
    }

    /// <summary>引导里点击"开始加速"：复用既有写入流程，把结果翻译成引导可展示的原因。</summary>
    private async Task<FirstRunStartResult> StartAccelerationFromGuideAsync()
    {
        _lastElevationResult = null;
        _applyIssue = null;

        await ApplyHosts();

        if (_lastElevationResult == ElevationResult.Started)
            return new FirstRunStartResult(FirstRunStartKind.Success);
        if (_lastElevationResult == ElevationResult.Cancelled)
            return new FirstRunStartResult(FirstRunStartKind.ElevationDenied);
        if (_lastElevationResult == ElevationResult.Failed)
            return new FirstRunStartResult(FirstRunStartKind.WriteFailed, ElevationService.LastError);

        if (IsHostsApplied) return new FirstRunStartResult(FirstRunStartKind.Success);
        if (_applyIssue == "所有数据源均不可用")
            return new FirstRunStartResult(FirstRunStartKind.MirrorUnavailable, _applyIssue);
        if (!string.IsNullOrEmpty(_applyIssue))
            return new FirstRunStartResult(FirstRunStartKind.WriteFailed, _applyIssue);

        return new FirstRunStartResult(FirstRunStartKind.Incomplete, StatusMessage);
    }

    /// <summary>引导里的两个勾选项（默认都关闭）落到设置里，并立即生效。</summary>
    private void SaveFirstRunGuideOptions(bool autoStart, bool diagnosticsEnabled)
    {
        _settings.AutoStart = autoStart;
        _settings.DiagnosticsEnabled = diagnosticsEnabled;
        SaveSettings();
    }

    private void SaveSettings()
    {
        _settings.Save();
    }

    private void ApplySettings()
    {
        // 更新 GitHub 访问速度测试间隔
        if (NetworkMonitor != null)
        {
            var monitorService = GithubAccelerator.UI.Services.GitHubLatencyMonitorService.Instance;
            monitorService.SetCheckInterval(TimeSpan.FromSeconds(10)); // 10秒一次
        }
    }

    private void InitializeSourcesFromConfig()
    {
        var hostSources = _hostsService.GetHostsSources();
        foreach (var source in hostSources)
        {
            var vm = new SourceStatusViewModel
            {
                Name = source.Name,
                Url = source.Url,
                IsHealthy = source.IsHealthy,
                IsDnsProbe = source.IsDnsProbe,
                ResponseTime = source.LastResponseTimeMs > 0 ? (long)source.LastResponseTimeMs : 0,
                MirrorUrls = source.MirrorUrls ?? Array.Empty<string>()
            };
            vm.OnApplyRequested += OnSingleSourceApplyRequested;
            vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(SourceStatusViewModel.IsSelected))
                {
                    OnPropertyChanged(nameof(HasSelectedSources));
                    OnPropertyChanged(nameof(SelectedSourcesCount));
                }
            };
            Sources.Add(vm);
        }
        UpdateBestSource();
        UpdateFilteredSources();
        IsLoading = false;
    }

    private void InitializeSources()
    {
        var metrics = _performanceMonitor.GetCurrentMetrics();
        foreach (var m in metrics)
        {
            var vm = new SourceStatusViewModel();
            vm.UpdateFromMetrics(m);
            vm.OnApplyRequested += OnSingleSourceApplyRequested;
            vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(SourceStatusViewModel.IsSelected))
                {
                    OnPropertyChanged(nameof(HasSelectedSources));
                    OnPropertyChanged(nameof(SelectedSourcesCount));
                }
            };
            Sources.Add(vm);
        }
        UpdateBestSource();
        UpdateFilteredSources();
        IsLoading = false;
    }

    private async void OnSingleSourceApplyRequested(SourceStatusViewModel source)
    {
        await ApplyHostsFromSource(source.Name, source.Url, source.MirrorUrls);
    }

    // ===== CanExecute 约束：高代价命令在执行期间禁用，避免重复触发 =====

    private bool CanApplyHosts() => !IsApplyingHosts;
    private bool CanRunSpeedTest() => !IsApplyingHosts;
    private bool CanRefreshSources() => !IsLoading;
    private bool CanClearHosts() => IsHostsApplied && !IsApplyingHosts;

    partial void OnIsAdminChanged(bool value)
    {
        ApplyHostsCommand.NotifyCanExecuteChanged();
        ClearAppliedHostsCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsLoadingChanged(bool value) => RefreshSourcesCommand.NotifyCanExecuteChanged();

    partial void OnIsHostsAppliedChanged(bool value)
    {
        _accelState.SyncFromFile(value);
        RaiseAccelerationStateChanged();
        OnPropertyChanged(nameof(AvailabilityConclusion));
        OnPropertyChanged(nameof(AvailabilityDetail));
    }

    [RelayCommand(CanExecute = nameof(CanClearHosts))]
    private async Task ClearAppliedHosts()
    {
        // 写系统 Hosts 需要管理员权限：按需提权，提权后由新实例接续本操作
        if (!IsAdmin)
        {
            _accelState.BeginElevation();
            RaiseAccelerationStateChanged();
            await RequestElevationAsync(LaunchIntent.ClearHosts);
            return;
        }

        try
        {
            var currentContent = await _hostsFileService.ReadHostsFileAsync();
            if (!_hostsFileService.IsGithubHostsApplied(currentContent))
            {
                IsHostsApplied = false;
                StatusMessage = "当前没有我们添加的内容";
                _notificationService.Info("Hosts 清除", "当前没有我们添加的内容");
                return;
            }

            // 只删除本项目标记块：块外内容（含用户手工修改）保持原样；
            // 若块内容与上次写入不一致，服务会返回"可能被手动修改"的说明。
            var beforeModified = _hostsFileService.IsBlockModified(currentContent);
            var success = await _hostsFileService.RestoreOriginalHostsAsync();

            if (success)
            {
                IsHostsApplied = false;
                CurrentHostsSource = string.Empty;
                StatusMessage = beforeModified
                    ? "已移除加速块（该块内容与上次写入不一致，可能被手动修改过；块外内容未改动）"
                    : "已清除我们添加的 Hosts 内容";
                _historyService.Record(OperationType.HostsApplied, "清除 Hosts 成功", true);
                _notificationService.Success("Hosts 清除",
                    beforeModified
                        ? "已移除加速块。检测到块内容与上次写入不同，可能是手动修改过；块外内容保持原样。"
                        : "已成功清除我们添加的 Hosts 内容");
                return;
            }

            StatusMessage = "清除失败：Hosts 文件未能原子替换，原文件保持不变";
            _notificationService.Error("Hosts 清除", StatusMessage);
        }
        catch (Exception ex)
        {
            StatusMessage = $"清除失败：{ex.Message}";
            _notificationService.Error("Hosts 清除", $"清除失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 加速写入的统一入口：首次写入前保存"首启前完整快照"，供高级恢复使用。
    /// </summary>
    private async Task<bool> ApplyAcceleratedHostsAsync(string hostsContent)
    {
        try
        {
            await _backupService.EnsureFirstRunSnapshotAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "创建首启前 Hosts 快照失败，继续应用加速");
        }

        // 写入前识别"我们那段被第三方软件改过"的情况，给用户一个可执行的查看入口。
        try
        {
            var current = await _hostsFileService.ReadHostsFileAsync();
            if (_hostsFileService.IsGithubHostsApplied(current) && _hostsFileService.IsBlockModified(current))
            {
                ShowFailureAdvice(FailureKind.HostsRewrittenByThirdParty);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "写入前检查 Hosts 标记块失败");
        }

        return await _hostsFileService.ApplyGithubHostsAsync(hostsContent);
    }

    [RelayCommand]
    private async Task ToggleMonitoring()
    {
        if (IsMonitoring)
        {
            _performanceMonitor.StopMonitoring();
            IsMonitoring = false;
            _updateTimer?.Stop();
            StatusMessage = "监控已停止";
            _historyService.Record(OperationType.MonitoringStopped, "停止监控");
        }
        else
        {
            _performanceMonitor.StartMonitoring();
            IsMonitoring = true;
            if (_settings.TestInterval > 0)
            {
                _updateTimer?.Start();
            }
            StatusMessage = "监控已启动，正在测试所有数据源...";
            _historyService.Record(OperationType.MonitoringStarted, "启动监控");
            _notificationService.Info("监控", "监控已启动，正在测试所有数据源");
        }
    }

    [RelayCommand(CanExecute = nameof(CanRefreshSources))]
    private async Task RefreshSources()
    {
        IsRefreshingSources = true;
        StatusMessage = "正在刷新数据源状态...";
        try
        {
            var metrics = await _performanceMonitor.TestAllSourcesAsync();
            ApplyMetrics(metrics);
            UpdateBestSource();
            await TestGitHubConnection();
            StatusMessage = $"已刷新 {Sources.Count} 个数据源状态";
            _historyService.Record(OperationType.SourcesRefreshed, $"刷新了 {Sources.Count} 个数据源");
        }
        finally
        {
            IsRefreshingSources = false;
        }
    }

    /// <summary>
    /// 把检测结果按 URL 写回唯一的 Sources 集合（不再按索引对齐：
    /// 监控结果按评分排序，而 Sources 按配置顺序，按索引会张冠李戴）。
    /// </summary>
    private void ApplyMetrics(IEnumerable<SourcePerformanceMetrics> metrics)
    {
        foreach (var metric in metrics)
        {
            var target = Sources.FirstOrDefault(s =>
                string.Equals(s.Url, metric.Url, StringComparison.OrdinalIgnoreCase));
            target?.UpdateFromMetrics(metric);
        }
    }

    [RelayCommand]
    private async Task TestGitHubConnection()
    {
        await TestGitHubConnectionAsync(showNotification: true);
    }

    private async Task TestGitHubConnectionAsync(bool showNotification = false)
    {
        try
        {
            var results = await _connectionService.TestAllConnectionsAsync();
            var successCount = results.Count(r => r.IsSuccess);
            var totalCount = results.Count;
            
            IsGitHubAccessible = successCount > 0;
            
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (IsGitHubAccessible)
                {
                    var avgLatency = results.Where(r => r.IsSuccess).Average(r => r.LatencyMs);
                    GitHubStatusText = $"直连：可访问 · 平均 {avgLatency:F0} ms";
                }
                else
                {
                    GitHubStatusText = "直连：无法访问";
                    if (showNotification)
                    {
                        StatusMessage = "⚠️ GitHub 无法访问！请检查网络或使用代理";
                        _notificationService.Warning("GitHub 连接", "无法访问 GitHub，TCP 端口可能被封锁");
                        ShowFailureAdvice(FailureKind.NoNetwork);
                    }
                }
            });
        }
        catch (Exception ex)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                GitHubStatusText = "直连：测试失败";
                if (showNotification)
                {
                    StatusMessage = $"⚠️ GitHub 连接测试失败: {ex.Message}";
                }
            });
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunSpeedTest))]
    private async Task RunSpeedTest()
    {
        if (!IsAdmin)
        {
            StatusMessage = "测速对比需要管理员权限";
            ToastService.Instance.Error("需要管理员权限", "临时修改 Hosts 进行对比测速需要管理员权限");
            return;
        }

        StatusMessage = "正在进行真正的应用前后对比测速...";
        StardustMonitorService.Instance.ReportInfo("SpeedTestStart", "用户启动测速对比");
        using var _trace = StardustMonitorService.Instance.BeginTrace("SpeedTest");

        var hostsWasApplied = IsHostsApplied;
        string? originalHostsBlock = null;
        string? originalHostsContent = null;

        try
        {
            originalHostsContent = await _hostsFileService.ReadHostsFileAsync();
            if (_hostsFileService.IsGithubHostsApplied(originalHostsContent))
            {
                originalHostsBlock = _hostsFileService.GetCurrentGithubHostsBlock(originalHostsContent);
            }

            // 1. 测量"应用前"：如果我们的 hosts 已应用，先临时移除，测真实无加速状态
            if (hostsWasApplied && !string.IsNullOrEmpty(originalHostsBlock))
            {
                if (!await _hostsFileService.RestoreOriginalHostsAsync())
                {
                    StatusMessage = "测速中止：无法临时移除 Hosts 加速块（原文件保持不变）";
                    return;
                }
                await _dnsFlusher.FlushDnsCacheAsync();
                await Task.Delay(600);
                IsHostsApplied = false;
            }

            var beforeSamples = await MeasureGitHubLatencySamplesAsync(3, "应用前");
            var beforeLatency = GetMedian(beforeSamples);

            // 2. 测量"应用后"：恢复或应用 hosts
            if (hostsWasApplied && !string.IsNullOrEmpty(originalHostsBlock))
            {
                if (!await _hostsFileService.ApplyGithubHostsAsync(originalHostsBlock))
                {
                    StatusMessage = "测速完成，但恢复加速块失败：请重新点击加速";
                }
                IsHostsApplied = true;
            }
            else
            {
                await ApplyBestHostsForSpeedTestAsync();
            }

            await _dnsFlusher.FlushDnsCacheAsync();
            await Task.Delay(600);

            var afterSamples = await MeasureGitHubLatencySamplesAsync(3, "应用后");
            var afterLatency = GetMedian(afterSamples);

            PreApplyLatency = FormatLatency(beforeLatency);
            PostApplyLatency = FormatLatency(afterLatency);

            if (beforeLatency > 0 && afterLatency > 0)
            {
                var improvement = ((double)(beforeLatency - afterLatency) / beforeLatency * 100);
                var improvementAbs = Math.Abs(improvement);
                if (improvementAbs < 5)
                {
                    LatencyImprovement = "持平";
                    ImprovementColor = Avalonia.Media.Color.Parse("#FFC107");
                }
                else if (improvement > 0)
                {
                    LatencyImprovement = $"提升 {improvement:F0}%";
                    ImprovementColor = Avalonia.Media.Color.Parse("#4CAF50");
                }
                else
                {
                    LatencyImprovement = $"降低 {improvementAbs:F0}%";
                    ImprovementColor = Avalonia.Media.Color.Parse("#FF5722");
                }
            }
            else
            {
                LatencyImprovement = "--";
                ImprovementColor = Avalonia.Media.Color.Parse("#FFFFFF");
            }

            StatusMessage = hostsWasApplied
                ? $"测速完成（单次请求）：加速前 {PreApplyLatency} → 加速后 {PostApplyLatency}"
                : $"测速完成并已开启网页加速（单次请求）：加速前 {PreApplyLatency} → 加速后 {PostApplyLatency}";
            _historyService.Record(OperationType.SettingsChanged, $"测速对比：{PreApplyLatency} → {PostApplyLatency}");

            StardustMonitorService.Instance.ReportInfo("SpeedTestResult",
                $"before={PreApplyLatency} after={PostApplyLatency} improvement={LatencyImprovement}");
        }
        catch (Exception ex)
        {
            StardustMonitorService.Instance.ReportError("SpeedTestException", "测速异常", ex);
            StatusMessage = $"测速失败：{ex.Message}";
            PreApplyLatency = "-- ms";
            PostApplyLatency = "-- ms";
            LatencyImprovement = "--";

            // 出现异常时尝试恢复原始 hosts（整体还原，不走标记块路径）
            if (!string.IsNullOrEmpty(originalHostsContent))
            {
                try
                {
                    await _hostsFileService.ReplaceHostsFileAsync(originalHostsContent);
                    IsHostsApplied = _hostsFileService.IsGithubHostsApplied(originalHostsContent);
                }
                catch (Exception restoreEx)
                {
                    StatusMessage = $"测速失败，且恢复 Hosts 失败：{restoreEx.Message}";
                }
            }
        }
    }

    private async Task<List<long>> MeasureGitHubLatencySamplesAsync(int count, string phase)
    {
        var samples = new List<long>();
        for (int i = 0; i < count; i++)
        {
            var latency = await MeasureGitHubLatencyAsync();
            if (latency > 0)
            {
                samples.Add(latency);
            }
            if (i < count - 1)
            {
                await Task.Delay(200); // 间隔 200ms，避免请求过于密集
            }
        }
        return samples;
    }

    private static long GetMedian(List<long> values)
    {
        if (values.Count == 0) return -1;
        var sorted = values.OrderBy(v => v).ToList();
        if (sorted.Count % 2 == 1)
        {
            return sorted[sorted.Count / 2];
        }
        return (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
    }

    private async Task<long> MeasureGitHubLatencyAsync()
    {
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            await client.GetAsync("https://github.com", HttpCompletionOption.ResponseHeadersRead);
            sw.Stop();
            return sw.ElapsedMilliseconds;
        }
        catch (TaskCanceledException)
        {
            return -1; // 超时
        }
        catch
        {
            return -1; // 无法连接
        }
    }

    private static string FormatLatency(long ms) => ms switch
    {
        < 0 => "⛔ 超时",
        < 300 => $"{ms} ms",
        < 1000 => $"{ms} ms",
        _ => $"{ms / 1000.0:F1}s"
    };

    private async Task ApplyBestHostsForSpeedTestAsync()
    {
        // 测速时也使用多源智能合并 + IP优选
        await ApplyHostsFromAllSources();
    }

    [RelayCommand(CanExecute = nameof(CanApplyHosts))]
    private async Task ApplyHosts()
    {
        // 写系统 Hosts 需要管理员权限：按需提权，提权后由新实例接续本操作
        if (!IsAdmin)
        {
            _accelState.BeginElevation();
            RaiseAccelerationStateChanged();
            await RequestElevationAsync(LaunchIntent.ApplyHosts);
            return;
        }

        if (!BeginAcceleration()) return;

        try
        {
            await ApplyHostsFromAllSources();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Hosts 应用失败：{ex.Message}";
            _historyService.Record(OperationType.HostsApplied, $"应用 Hosts 异常：{ex.Message}", false, ex.StackTrace);
            _notificationService.Error("Hosts 应用", $"应用失败：{ex.Message}");
        }
        finally
        {
            FinishAcceleration();
        }
    }

    /// <summary>
    /// 需要管理员权限时按需提权：以管理员身份重启自身，并带上原操作意图。
    /// 用户取消 UAC 时保持当前进程可用，并明确告知系统 Hosts 未被修改。
    /// </summary>
    private Task RequestElevationAsync(LaunchIntent intent)
    {
        StatusMessage = "需要管理员权限写入系统 Hosts，正在请求提权…";
        Log.Information("[Elevation] 按需提权，intent={Intent}", intent);

        var result = ElevationService.RelaunchElevated(intent);
        _lastElevationResult = result;
        switch (result)
        {
            case ElevationResult.Started:
                _accelState.EndElevation(true);
                RaiseAccelerationStateChanged();
                Log.Information("[Elevation] 已启动管理员实例，当前实例退出");
                // 释放单实例互斥体，交给提权后的实例接续执行
                if (Avalonia.Application.Current?.ApplicationLifetime
                    is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                {
                    desktop.Shutdown();
                }
                else
                {
                    Environment.Exit(0);
                }
                break;

            case ElevationResult.Cancelled:
                _accelState.EndElevation(false);
                RaiseAccelerationStateChanged();
                StatusMessage = "已取消提权，系统 Hosts 未被修改";
                Log.Information("[Elevation] 用户取消 UAC，未修改 Hosts");
                _notificationService.Info("已取消提权",
                    "Hosts 未被修改。可以稍后重试，或右键以管理员身份运行本应用。");
                ShowFailureAdvice(FailureKind.ElevationDenied);
                break;

            default:
                _accelState.EndElevation(false);
                RaiseAccelerationStateChanged();
                StatusMessage = $"提权失败：{ElevationService.LastError}";
                Log.Warning("[Elevation] 提权失败：{Error}", ElevationService.LastError);
                _notificationService.Error("提权失败",
                    $"无法以管理员身份重启：{ElevationService.LastError}");
                ShowFailureAdvice(FailureKind.ElevationDenied);
                break;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 提权重启后接续用户上次未完成的操作（由主窗口打开时调用）。
    /// </summary>
    public async Task RunPendingLaunchIntentAsync()
    {
        var intent = LaunchContext.ConsumeIntent();
        if (intent == LaunchIntent.None) return;

        if (!IsAdmin)
        {
            Log.Warning("[Elevation] 接续意图 {Intent} 失败：仍未获得管理员权限", intent);
            _notificationService.Info("未获得管理员权限", "Hosts 未被修改，请稍后重试。");
            return;
        }

        StatusMessage = "已获得管理员权限，正在继续上次操作…";
        Log.Information("[Elevation] 接续提权前的操作：{Intent}", intent);

        switch (intent)
        {
            case LaunchIntent.ApplyHosts:
                await ApplyHosts();
                break;
            case LaunchIntent.ClearHosts:
                await ClearAppliedHosts();
                break;
        }
    }

    /// <summary>
    /// 一键智能优化：自动拉取全部 3 个数据源，合并所有 IP 候选，
    /// DNS 实时解析 + HTTPS 探测，为每个域名选择最优 IP。
    /// </summary>
    private async Task ApplyHostsFromAllSources()
    {
        using var _trace = StardustMonitorService.Instance.BeginTrace("ApplyHosts", "AllSources");

        StatusMessage = "正在从全部数据源拉取 Hosts（并行）...";
        XTrace.WriteLine("[ApplyHosts/All] 开始从全部源拉取 Hosts");
        var totalSteps = _settings.AutoFlushDns ? 5 : 4;
        SetAccelerationProgress(0, totalSteps, "拉取数据源");

        // 1. 并行拉取所有源
        var sources = GithubHostsService.HostsSources;
        var fetchTasks = sources.Select(async source =>
        {
            try
            {
                var (content, usedUrl, error) = await FetchHostsContentAsync(source.Name, source.Url, source.MirrorUrls);
                XTrace.WriteLine($"[ApplyHosts/All] {source.Name}: content={content?.Length ?? 0} err={error}");
                return (source.Name, Content: content, Error: error);
            }
            catch (Exception ex)
            {
                XTrace.WriteLine($"[ApplyHosts/All] {source.Name} 拉取异常：{ex.Message}");
                return (source.Name, Content: (string?)null, Error: ex.Message);
            }
        });

        var fetchResults = await Task.WhenAll(fetchTasks);

        // 取消检查点：拉取完成后不再继续（不会写入 Hosts）
        AbortIfCanceled();
        if (!_accelState.IsBusy) return;
        var validContents = fetchResults
            .Where(r => !string.IsNullOrEmpty(r.Content))
            .Select(r => r.Content!)
            .ToList();

        if (validContents.Count == 0)
        {
            _applyIssue = "所有数据源均不可用";
            StatusMessage = "所有加速线路都获取失败，请检查网络或恢复原始访问";
            AllSourcesFailureText = AllSourcesFailureReport.Build(
                DateTime.Now,
                fetchResults.Select(r => (r.Name, r.Error)));
            HasAllSourcesFailure = true;
            _notificationService.Error("Hosts 应用", "所有数据源均不可用");

            // 已应用加速但全部源挂掉：不能继续显示"已加速"，改为"已加速但源不可用"。
            if (IsHostsApplied)
            {
                _accelState.MarkSourceUnavailable("全部加速线路不可达");
                RaiseAccelerationStateChanged();
            }

            ShowFailureAdvice(FailureKind.AllMirrorsUnreachable);
            return;
        }

        XTrace.WriteLine($"[ApplyHosts/All] 成功拉取 {validContents.Count}/{sources.Length} 个源");
        SetAccelerationProgress(1, totalSteps, $"拉取完成（{validContents.Count}/{sources.Length} 个源）");
        StatusMessage = $"已拉取 {validContents.Count} 个源，正在合并并优化 IP（实时探测中，请稍候）...";

        // 2. 合并所有 hosts 内容
        var mergedHosts = string.Join("\n", validContents);
        SetAccelerationProgress(2, totalSteps, "合并完成");
        XTrace.WriteLine($"[ApplyHosts/All] 合并后内容长度={mergedHosts.Length}");

        // 3. IP 优化（DNS解析 + HTTPS探测 + 择优）
        try
        {
            var optimizer = new HostsOptimizerService
            {
                IncludeDnsResolvedIps = true, // DNS 实时解析获取新鲜 IP，避免源内 IP 被封锁
                ProbeTimeoutSeconds = 5       // 放宽超时，减少间歇性 IP 假阴性
            };
            optimizer.OnLog += msg =>
            {
                StatusMessage = msg;
                XTrace.WriteLine($"[ApplyHosts/Optimizer] {msg}");
            };
            var optimizedContent = await optimizer.OptimizeAsync(mergedHosts, _applyCts?.Token ?? default);
            XTrace.WriteLine($"[ApplyHosts/All] 优化完成，内容长度={optimizedContent.Length}");
            SetAccelerationProgress(3, totalSteps, "IP 优选完成");

            // 取消检查点：优化完成后、写 Hosts 之前
            AbortIfCanceled();
            if (!_accelState.IsBusy) return;

            // 4. 写入 hosts 文件（原子替换；取消只发生在写入前，不会留下半写状态）
            var success = await ApplyAcceleratedHostsAsync(optimizedContent);
            if (success) SetAccelerationProgress(4, totalSteps, "Hosts 写入完成");

            if (success)
            {
                IsHostsApplied = true;
                CurrentHostsSource = "多源智能合并";

                StardustMonitorService.Instance.ReportInfo("HostsApplied",
                    $"source=AllSources merged={validContents.Count}/{sources.Length}");

                try
                {
                    if (_settings.AutoFlushDns)
                    {
                        await _dnsFlusher.FlushDnsCacheAsync();
                        SetAccelerationProgress(totalSteps, totalSteps, "DNS 缓存已刷新");
                        StatusMessage = "Hosts 已成功应用（多源智能合并 + IP优选，DNS缓存已刷新）";
                    }
                    else
                    {
                        StatusMessage = "Hosts 已成功应用（多源智能合并 + IP优选）";
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "刷新DNS缓存失败");
                    _applyIssue = "DNS 缓存刷新失败（Hosts 已写入，可手动 ipconfig /flushdns）";
                    StatusMessage = "Hosts 已成功应用（多源智能合并 + IP优选，建议手动刷新DNS缓存）";
                }

                _historyService.Record(OperationType.HostsApplied,
                    $"多源智能合并：{validContents.Count}源 → IP优选 → 应用成功", true);
                _statsService.RecordSourceUsed("AllSources", "merged", 0, true);
                _notificationService.Success("Hosts 应用",
                    $"已从 {validContents.Count} 个源智能合并并优化 Hosts");

                // 自动触发子域健康检查
                _ = TriggerAutoHealthCheckAsync();
            }
            else
            {
                _applyIssue = "Hosts 写入失败（权限不足或文件被占用）";
                StatusMessage = "Hosts 应用失败，请以管理员身份运行";
                _historyService.Record(OperationType.HostsApplied, "应用 Hosts 失败：权限不足", false);
                _notificationService.Error("Hosts 应用", "应用失败，请以管理员身份运行");
                StardustMonitorService.Instance.ReportError("HostsApplyFailed", "权限不足或写入失败");
                ShowFailureAdvice(FailureKind.HostsWriteDenied);
            }
        }
        catch (Exception ex)
        {
            XTrace.WriteLine($"[ApplyHosts/All] 优化异常：{ex.GetType().Name}: {ex.Message}");
            XTrace.WriteLine(ex.ToString());
            Log.Warning(ex, "Hosts IP 优化失败");
            StardustMonitorService.Instance.ReportError("HostsOptimizeFailed", ex.Message, ex);

            // 优化失败，退化为直接应用合并后的原始内容
            AbortIfCanceled();
            if (!_accelState.IsBusy) return;

            _applyIssue = "IP 优选未完成（已应用原始合并内容）";
            StatusMessage = "IP 优化失败，使用原始合并内容...";
            var success = await ApplyAcceleratedHostsAsync(mergedHosts);
            if (success)
            {
                IsHostsApplied = true;
                CurrentHostsSource = "多源合并（未优化）";
                StatusMessage = "Hosts 已应用（多源合并，未优化）";
                _notificationService.Warning("Hosts 应用", "IP 优选跳过，已应用原始合并内容");

                // 自动触发子域健康检查
                _ = TriggerAutoHealthCheckAsync();
            }
            else
            {
                _applyIssue = "Hosts 写入失败（权限不足或文件被占用）";
                ShowFailureAdvice(FailureKind.HostsWriteDenied);
            }
        }
    }
    
    private async Task ApplyHostsFromSource(string sourceName, string sourceUrl, string[]? mirrorUrls = null)
    {
        if (!BeginAcceleration()) return;

        StatusMessage = $"正在从 {sourceName} 获取 Hosts...";

        using var _trace = StardustMonitorService.Instance.BeginTrace("ApplyHosts", sourceName);

        var totalSteps = _settings.AutoFlushDns ? 4 : 3;
        SetAccelerationProgress(0, totalSteps, $"拉取 {sourceName}");

        try
        {
            var (hostsContent, usedUrl, fetchError) = await FetchHostsContentAsync(sourceName, sourceUrl, mirrorUrls);

            AbortIfCanceled();
            if (!_accelState.IsBusy) return;

            SetAccelerationProgress(1, totalSteps, "拉取完成");

            if (string.IsNullOrEmpty(hostsContent))
            {
                StatusMessage = string.IsNullOrEmpty(fetchError)
                    ? $"获取 Hosts 内容为空"
                    : $"获取 Hosts 失败：{fetchError}";
                _notificationService.Warning("Hosts 应用", $"{sourceName} 获取内容失败");
                return;
            }

            // 对非 DNS 探测源进行 IP 优化（实时探测选择最佳 IP）
            if (!sourceUrl.StartsWith("dns:"))
            {
                try
                {
                    StatusMessage = "正在优化 IP 映射（实时探测中，请稍候）...";
                    XTrace.WriteLine($"[ApplyHosts] 开始 IP 优化，source={sourceName} url={sourceUrl}");
                    var optimizer = new HostsOptimizerService
                {
                    IncludeDnsResolvedIps = true,
                    ProbeTimeoutSeconds = 5
                };
                    optimizer.OnLog += msg =>
                    {
                        StatusMessage = msg;
                        XTrace.WriteLine($"[ApplyHosts/Optimizer] {msg}");
                    };
                    hostsContent = await optimizer.OptimizeAsync(hostsContent, _applyCts?.Token ?? default);
                    SetAccelerationProgress(2, totalSteps, "IP 优选完成");
                    XTrace.WriteLine($"[ApplyHosts] IP 优化完成，新内容长度={hostsContent.Length}");
                    StardustMonitorService.Instance.ReportInfo("HostsOptimized", $"source={sourceName}");
                }
                catch (Exception ex)
                {
                    XTrace.WriteLine($"[ApplyHosts] IP 优化异常：{ex.GetType().Name}: {ex.Message}");
                    XTrace.WriteLine(ex.ToString());
                    Log.Warning(ex, "Hosts IP 优化失败，将使用原始内容");
                    StardustMonitorService.Instance.ReportError("HostsOptimizeFailed", ex.Message, ex);
                    // 优化失败不阻断，继续使用原始内容
                    _applyIssue = "IP 优选未完成（已应用原始内容）";
                }
            }
            else
            {
                XTrace.WriteLine($"[ApplyHosts] 跳过 IP 优化（DNS 探测源），source={sourceName}");
            }

            AbortIfCanceled();
            if (!_accelState.IsBusy) return;

            var success = await ApplyAcceleratedHostsAsync(hostsContent);
            if (success) SetAccelerationProgress(3, totalSteps, "Hosts 写入完成");

            if (success)
            {
                IsHostsApplied = true;
                CurrentHostsSource = sourceName;

                StardustMonitorService.Instance.ReportInfo("HostsApplied",
                    $"source={sourceName} url={usedUrl ?? sourceUrl} mirrors={(mirrorUrls?.Length ?? 0)}");

                try
                {
                    if (_settings.AutoFlushDns)
                    {
                        await _dnsFlusher.FlushDnsCacheAsync();
                        SetAccelerationProgress(totalSteps, totalSteps, "DNS 缓存已刷新");
                        StatusMessage = $"Hosts 已成功应用，源：{sourceName}（DNS缓存已刷新）";
                    }
                    else
                    {
                        StatusMessage = $"Hosts 已成功应用，源：{sourceName}";
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "刷新DNS缓存失败");
                    _applyIssue = "DNS 缓存刷新失败（Hosts 已写入）";
                    StatusMessage = $"Hosts 已成功应用，源：{sourceName}（建议手动刷新DNS缓存）";
                }

                var usedMirrorHint = usedUrl != null && usedUrl != sourceUrl ? $" (镜像: {usedUrl})" : "";
                _historyService.Record(OperationType.HostsApplied, $"从 {sourceName} 应用 Hosts 成功{usedMirrorHint}", true);
                _statsService.RecordSourceUsed(sourceName, usedUrl ?? sourceUrl, 0, true);
                _notificationService.Success("Hosts 应用", $"已从 {sourceName} 成功应用 Hosts");
            }
            else
            {
                _applyIssue = "Hosts 写入失败（权限不足或文件被占用）";
                StatusMessage = "Hosts 应用失败，请以管理员身份运行";
                _historyService.Record(OperationType.HostsApplied, "应用 Hosts 失败：权限不足", false);
                _notificationService.Error("Hosts 应用", "应用失败，请以管理员身份运行");
                StardustMonitorService.Instance.ReportError("HostsApplyFailed", "权限不足或写入失败");
                ShowFailureAdvice(FailureKind.HostsWriteDenied);
            }
        }
        catch (Exception ex)
        {
            StardustMonitorService.Instance.ReportError("HostsApplyException", sourceName, ex);
            StatusMessage = $"Hosts 应用失败：{ex.Message}";
            _historyService.Record(OperationType.HostsApplied, $"应用 Hosts 异常：{ex.Message}", false, ex.StackTrace);
            _statsService.RecordTimeout(sourceName, sourceUrl);
            _notificationService.Error("Hosts 应用", $"应用失败：{ex.Message}");
        }
        finally
        {
            FinishAcceleration();
        }
    }

    /// <summary>
    /// 统一的 Hosts 内容获取方法：处理 DNS 探测源和普通 HTTP 源（含镜像故障转移）。
    /// 返回 (content, usedUrl, error)。usedUrl 用于日志记录实际使用的镜像 URL。
    /// </summary>
    private async Task<(string content, string? usedUrl, string? error)> FetchHostsContentAsync(
        string sourceName, string sourceUrl, string[]? mirrorUrls)
    {
        // DNS 探测源走特殊路径
        if (sourceUrl.StartsWith("dns:") || sourceUrl == "chinaz:dns")
        {
            StatusMessage = $"正在通过多源 DNS 查询 GitHub IP...";
            var chinazService = ChinazDnsService.Instance;
            var content = await chinazService.GenerateHostsContentAsync();
            return (content, sourceUrl, null);
        }

        // 普通 HTTP 源：主 URL + 镜像 URL 依次尝试
        var candidates = new List<string> { sourceUrl };
        if (mirrorUrls != null && mirrorUrls.Length > 0)
        {
            candidates.AddRange(mirrorUrls.Where(u => !string.IsNullOrEmpty(u) && u != sourceUrl));
        }

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        Exception? lastException = null;

        for (int i = 0; i < candidates.Count; i++)
        {
            var url = candidates[i];
            try
            {
                if (i > 0)
                {
                    StatusMessage = $"主 URL 失败，正在尝试镜像 ({i}/{candidates.Count - 1})...";
                }
                var content = await httpClient.GetStringAsync(url);
                if (!string.IsNullOrEmpty(content))
                {
                    return (content, url, null);
                }
            }
            catch (Exception ex)
            {
                lastException = ex;
                Log.Warning(ex, "从 {SourceName} 获取 Hosts 失败 (URL: {Url})", sourceName, url);
            }
        }

        return (string.Empty, null, lastException?.Message ?? "所有 URL 均失败");
    }

    [RelayCommand]
    private async Task ApplySelectedSources()
    {
        var selectedSources = Sources.Where(s => s.IsSelected).ToList();
        if (selectedSources.Count == 0)
        {
            StatusMessage = "请先选择要应用的数据源";
            _notificationService.Warning("数据源选择", "请先选择要应用的数据源");
            return;
        }

        if (!BeginAcceleration()) return;

        try
        {
            var allHostsContent = new System.Text.StringBuilder();
            allHostsContent.AppendLine("# GitHub Accelerator - Applied Sources");
            allHostsContent.AppendLine($"# Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            allHostsContent.AppendLine();

            var totalSteps = selectedSources.Count + (_settings.AutoFlushDns ? 3 : 2);
            var doneSteps = 0;
            SetAccelerationProgress(0, totalSteps, "准备拉取数据源");

            foreach (var source in selectedSources)
            {
                AbortIfCanceled();
                if (!_accelState.IsBusy) return;

                StatusMessage = $"正在从 {source.Name} 获取 Hosts...";
                try
                {
                    var (content, _, _) = await FetchHostsContentAsync(source.Name, source.Url, source.MirrorUrls);
                    doneSteps++;
                    SetAccelerationProgress(doneSteps, totalSteps, $"已拉取 {source.Name}");

                    if (!string.IsNullOrEmpty(content))
                    {
                        allHostsContent.AppendLine($"# Source: {source.Name}");
                        allHostsContent.AppendLine(content);
                        allHostsContent.AppendLine();
                    }
                }
                catch (Exception ex)
                {
                    _historyService.Record(OperationType.HostsApplied, $"从 {source.Name} 获取失败：{ex.Message}", false);
                }
            }

            var mergedContent = allHostsContent.ToString();

            // IP 优化（DNS解析 + HTTPS探测 + 择优）
            StatusMessage = "正在优化 IP 映射（实时探测中，请稍候）...";
            try
            {
                var optimizer = new HostsOptimizerService
                {
                    IncludeDnsResolvedIps = true,
                    ProbeTimeoutSeconds = 5
                };
                optimizer.OnLog += msg =>
                {
                    StatusMessage = msg;
                    XTrace.WriteLine($"[ApplySel/Optimizer] {msg}");
                };
                mergedContent = await optimizer.OptimizeAsync(mergedContent, _applyCts?.Token ?? default);
                SetAccelerationProgress(++doneSteps, totalSteps, "IP 优选完成");
            }
            catch (Exception ex)
            {
                XTrace.WriteLine($"[ApplySel] 优化失败，使用原始内容：{ex.Message}");
                _applyIssue = "IP 优选未完成（已应用原始内容）";
                SetAccelerationProgress(++doneSteps, totalSteps, "IP 优选跳过");
            }

            AbortIfCanceled();
            if (!_accelState.IsBusy) return;

            var success = await ApplyAcceleratedHostsAsync(mergedContent);
            if (success) SetAccelerationProgress(++doneSteps, totalSteps, "Hosts 写入完成");

            if (success)
            {
                IsHostsApplied = true;
                CurrentHostsSource = $"{selectedSources.Count} 个源";
                
                try
                {
                    if (_settings.AutoFlushDns)
                    {
                        await _dnsFlusher.FlushDnsCacheAsync();
                        SetAccelerationProgress(++doneSteps, totalSteps, "DNS 缓存已刷新");
                        StatusMessage = $"已成功应用 {selectedSources.Count} 个数据源（DNS缓存已刷新）";
                    }
                    else
                    {
                        StatusMessage = $"已成功应用 {selectedSources.Count} 个数据源";
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "刷新DNS缓存失败");
                    _applyIssue = "DNS 缓存刷新失败（Hosts 已写入）";
                    StatusMessage = $"已成功应用 {selectedSources.Count} 个数据源（建议手动刷新DNS缓存）";
                }
                
                _historyService.Record(OperationType.HostsApplied, $"应用 {selectedSources.Count} 个数据源成功", true);
                _notificationService.Success("Hosts 应用", $"已成功应用 {selectedSources.Count} 个数据源");
            }
            else
            {
                _applyIssue = "Hosts 写入失败（权限不足或文件被占用）";
                StatusMessage = "Hosts 应用失败，请以管理员身份运行";
                _notificationService.Error("Hosts 应用", "应用失败，请以管理员身份运行");
                ShowFailureAdvice(FailureKind.HostsWriteDenied);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"应用失败：{ex.Message}";
            _notificationService.Error("Hosts 应用", $"应用失败：{ex.Message}");
        }
        finally
        {
            FinishAcceleration();
        }
    }

    [RelayCommand]
    private void SelectAllSources()
    {
        foreach (var source in Sources)
        {
            source.IsSelected = true;
        }
        OnPropertyChanged(nameof(HasSelectedSources));
        OnPropertyChanged(nameof(SelectedSourcesCount));
    }

    [RelayCommand]
    private void DeselectAllSources()
    {
        foreach (var source in Sources)
        {
            source.IsSelected = false;
        }
        OnPropertyChanged(nameof(HasSelectedSources));
        OnPropertyChanged(nameof(SelectedSourcesCount));
    }

    private void UpdateBestSource()
    {
        // 推荐卡与列表、计数使用同一个集合：只推荐列表里确实存在且评分 > 0 的数据源
        var best = SelectRecommendedSource(Sources);

        if (best != null)
        {
            BestSourceName = best.Name;
            BestSourceResponseTime = best.ResponseTime;
            BestSourceScore = best.Score;
        }
        else
        {
            BestSourceName = string.Empty;
            BestSourceResponseTime = 0;
            BestSourceScore = 0;
        }
    }

    /// <summary>
    /// 后台定时 Hosts 优化刷新（每30分钟），静默执行不弹通知。
    /// 仅在已应用 Hosts 且管理员权限下生效。
    /// </summary>
    private void StartPeriodicHostsRefresh()
    {
        _periodicHostsRefreshTimer?.Stop();
        _periodicHostsRefreshTimer?.Dispose();
        _periodicHostsRefreshTimer = new System.Timers.Timer(30 * 60 * 1000); // 30分钟
        _periodicHostsRefreshTimer.Elapsed += async (s, e) =>
        {
            if (!IsHostsApplied || !IsAdmin || IsApplyingHosts) return;
            try
            {
                XTrace.WriteLine("[PeriodicRefresh] 定时刷新 Hosts 优化...");
                await ApplyHostsFromAllSources();
                XTrace.WriteLine("[PeriodicRefresh] 定时刷新完成");
            }
            catch (Exception ex)
            {
                XTrace.WriteLine($"[PeriodicRefresh] 异常：{ex.Message}");
            }
        };
        _periodicHostsRefreshTimer.Start();
        XTrace.WriteLine("[PeriodicRefresh] 定时刷新已启动，间隔30分钟");
    }

    private async Task AutoUpdateAsync()
    {
        if (!IsMonitoring) return;

        try
        {
            var metrics = _performanceMonitor.GetCurrentMetrics();
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                ApplyMetrics(metrics);
                UpdateBestSource();
            });

            // 如果开启了自动应用Hosts，且Hosts未应用，且已经有有效的性能数据，则自动应用
            if (_settings.AutoApplyHosts && !IsHostsApplied && metrics.Any(m => m.AverageResponseTimeMs > 0))
            {
                await ApplyHosts();
            }

            // 如果开启了自动切换最佳源，且当前有应用中的源，则检查是否需要切换
            if (_settings.AutoSwitchBestSource && IsHostsApplied && !string.IsNullOrEmpty(CurrentHostsSource))
            {
                var currentSource = Sources.FirstOrDefault(s => s.Name == CurrentHostsSource);
                var bestSource = _performanceMonitor.GetBestSource();
                
                // 如果找到了最佳源，且最佳源比当前源快 20ms 以上，则切换
                if (bestSource != null && bestSource.AverageResponseTimeMs > 0)
                {
                    if (currentSource == null || 
                        bestSource.AverageResponseTimeMs < currentSource.ResponseTime - 20)
                    {
                        StatusMessage = $"自动切换到更快的源：{bestSource.Name} ({bestSource.AverageResponseTimeMs}ms)";
                        await ApplyHostsFromSource(bestSource.Name, bestSource.Url);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "自动更新时发生错误");
        }
    }

    private async void CheckHostsStatus()
    {
        try
        {
            var hostsContent = await _hostsFileService.ReadHostsFileAsync();
            IsHostsApplied = _hostsFileService.IsGithubHostsApplied(hostsContent);
            HostsContent = hostsContent;
            if (IsHostsApplied)
            {
                StatusMessage = "Hosts 已应用";
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "检查Hosts状态时发生错误");
        }
    }

    /// <summary>重读 Hosts 文本，供「Hosts 与备份」页内刷新使用；不切换页面。</summary>
    [RelayCommand]
    private async Task RefreshHostsContent()
    {
        try
        {
            var hostsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");
            HostsContent = await File.ReadAllTextAsync(hostsPath);
            IsHostsApplied = _hostsFileService.IsGithubHostsApplied(HostsContent);
            StatusMessage = "已加载 Hosts 文件内容";
            _historyService.Record(OperationType.HostsViewed, "查看 Hosts 文件内容");
        }
        catch (Exception ex)
        {
            StatusMessage = $"读取 Hosts 失败：{ex.Message}";
            _historyService.Record(OperationType.HostsViewed, $"查看 Hosts 失败：{ex.Message}", false);
        }
    }

    [RelayCommand]
    private void OpenGitHubOriginal()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com",
                UseShellExecute = true
            });
            _historyService.Record(OperationType.HostsViewed, "打开 github.com 原始链接");
        }
        catch (Exception ex)
        {
            StatusMessage = $"打开浏览器失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenHostsInNotepad()
    {
        try
        {
            var hostsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");
            Process.Start(new ProcessStartInfo
            {
                FileName = hostsPath,
                UseShellExecute = true
            });
            StatusMessage = "已在记事本中打开 Hosts 文件";
            _historyService.Record(OperationType.HostsOpenedInNotepad, "用记事本打开 Hosts 文件");
        }
        catch (Exception ex)
        {
            StatusMessage = $"打开 Hosts 失败：{ex.Message}";
            _historyService.Record(OperationType.HostsOpenedInNotepad, $"打开 Hosts 失败：{ex.Message}", false);
        }
    }

    /// <summary>
    /// 由 View 层注入的剪贴板写入委托（Hosts 视图复制用）
    /// </summary>
    public Func<string, Task>? SetClipboardTextAsync { get; set; }

    /// <summary>
    /// 复制当前 Hosts 内容到剪贴板（含 GitHub 加速块及注释，便于反馈）
    /// </summary>
    [RelayCommand]
    private async Task CopyHostsContentAsync()
    {
        if (string.IsNullOrEmpty(HostsContent))
        {
            StatusMessage = "Hosts 内容为空";
            return;
        }

        try
        {
            if (SetClipboardTextAsync == null)
            {
                StatusMessage = "剪贴板未初始化";
                return;
            }
            await SetClipboardTextAsync(HostsContent);
            StatusMessage = "✓ Hosts 内容已复制到剪贴板";
            _historyService.Record(OperationType.HostsViewed, "复制 Hosts 内容到剪贴板");
        }
        catch (Exception ex)
        {
            StatusMessage = $"复制失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task FlushDnsCacheAsync()
    {
        try
        {
            StatusMessage = "正在刷新DNS缓存...";
            await _dnsFlusher.FlushDnsCacheAsync();
            StatusMessage = "DNS缓存已成功刷新";
            _historyService.Record(OperationType.SettingsChanged, "手动刷新DNS缓存成功", true);
            _notificationService.Success("DNS刷新", "DNS缓存已成功刷新");
        }
        catch (Exception ex)
        {
            StatusMessage = $"DNS缓存刷新失败：{ex.Message}";
            _notificationService.Error("DNS刷新", $"刷新失败：{ex.Message}，请以管理员身份运行");
        }
    }

    private async Task AutoApplyHostsOnStartup()
    {
        try
        {
            if (IsHostsApplied)
            {
                Log.Information("Hosts 已应用，跳过自动应用");
                return;
            }

            // 先测试 GitHub 当前是否可访问
            Log.Information("启动时测试 GitHub 连通性...");
            await TestGitHubConnectionAsync();
            await Task.Delay(1000); // 等待测试完成

            if (IsGitHubAccessible)
            {
                Log.Information("GitHub 当前可访问，跳过自动应用 Hosts");
                StatusMessage = "GitHub 当前可访问，无需应用 Hosts 加速";
                return;
            }

            Log.Information("GitHub 不可访问，启动自动应用Hosts");
            await ApplyHosts();
        }
        catch (Exception ex)
        {
            Log.Error("启动自动应用Hosts失败: {0}", ex.Message);
        }
    }

    /// <summary>
    /// 自动触发子域健康检查（Hosts 应用后延迟 2 秒执行，等待 DNS 生效）。
    /// </summary>
    private async Task TriggerAutoHealthCheckAsync()
    {
        try
        {
            await Task.Delay(2000); // 等待 DNS 缓存刷新生效

            if (HealthCheck == null)
            {
                HealthCheck = new HealthCheckViewModel();
            }

            XTrace.WriteLine("[AutoHealthCheck] 自动触发子域健康检查...");
            await HealthCheck.RunCheckCommand.ExecuteAsync(null);
            XTrace.WriteLine("[AutoHealthCheck] 健康检查完成");
        }
        catch (Exception ex)
        {
            XTrace.WriteLine($"[AutoHealthCheck] 异常：{ex.Message}");
        }
    }

    /// <summary>
    /// 释放所有非托管资源：取消事件订阅、停止并 Dispose 所有 Timer、Dispose 子 ViewModel。
    /// </summary>
    public void Dispose()
    {
        // 1. 取消事件订阅
        _historyService.OnOperationRecorded -= OnOperationRecorded;
        _notificationService.OnNotification -= OnNotificationReceived;

        // 2. 停止并 Dispose 所有 Timer
        _updateTimer?.Stop();
        _updateTimer?.Dispose();
        _githubConnectivityTimer?.Stop();
        _githubConnectivityTimer?.Dispose();
        _periodicHostsRefreshTimer?.Stop();
        _periodicHostsRefreshTimer?.Dispose();

        // 3. Dispose 持有的子 ViewModel（如果实现了 IDisposable）
        //    NetworkMonitorViewModel 持有 GitHubLatencyViewModel，后者已实现 IDisposable
        //    使用 as 模式安全处理：当前 LogViewerViewModel/NetworkMonitorViewModel/HealthCheckViewModel 未直接实现 IDisposable，
        //    未来若实现将自动生效，无需修改此处
        (LogViewer as IDisposable)?.Dispose();
        (NetworkMonitor as IDisposable)?.Dispose();
        (HealthCheck as IDisposable)?.Dispose();
    }
}

public class SerilogLoggerAdapter<T> : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Serilog.Log.Write((Serilog.Events.LogEventLevel)(int)logLevel, exception, "{Message}", formatter(state, exception));
    }
}
