using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GithubAccelerator.Services;
using GithubAccelerator.UI.Services;
using GithubAccelerator.UI.Views;
using Microsoft.Extensions.Logging;

namespace GithubAccelerator.UI.ViewModels;

public partial class SourceStatusViewModel : ObservableObject
{
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

    public void UpdateFromMetrics(SourcePerformanceMetrics metrics)
    {
        Name = metrics.Name;
        Url = metrics.Url;
        ResponseTime = (long)metrics.AverageResponseTimeMs;
        Score = metrics.OverallScore;
        IsHealthy = metrics.SuccessRate > 0.5;
        LastTestTime = metrics.LastTestTime;
        OnPropertyChanged(nameof(ResponseTimeText));
        OnPropertyChanged(nameof(ScoreText));
        OnPropertyChanged(nameof(LastTestTimeText));
        OnPropertyChanged(nameof(StatusColor));
        OnPropertyChanged(nameof(ResponseTimeColor));
        OnPropertyChanged(nameof(ScoreColor));
    }
}

public partial class MainWindowViewModel : ObservableObject
{
    private readonly ISourcePerformanceMonitor _performanceMonitor;
    private readonly GithubHostsService _hostsService;
    private readonly IHostsFileService _hostsFileService;
    private readonly SettingsViewModel _settings;
    private readonly SourceStatisticsService _statsService;
    private System.Timers.Timer? _updateTimer;
    private System.Timers.Timer? _autoUpdateTimer;

    [ObservableProperty]
    private ObservableCollection<SourceStatusViewModel> _sources = new();

    [ObservableProperty]
    private bool _isMonitoring;

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
    private PerformanceChartViewModel? _performanceChart;

    [ObservableProperty]
    private bool _isPerformanceChartVisible;

    [ObservableProperty]
    private GitHubLatencyViewModel? _gitHubLatency;

    [ObservableProperty]
    private bool _isGitHubLatencyVisible;

    [ObservableProperty]
    private string _hostsContent = string.Empty;

    [ObservableProperty]
    private bool _isDarkMode;

    [ObservableProperty]
    private ObservableCollection<OperationRecord> _operationHistory = new();

    [ObservableProperty]
    private bool _hasNotifications;

    [ObservableProperty]
    private string _latestNotification = string.Empty;

    public bool HasSelectedSources => Sources.Any(s => s.IsSelected);

    public int SelectedSourcesCount => Sources.Count(s => s.IsSelected);

    private readonly OperationHistoryService _historyService = OperationHistoryService.Instance;
    private readonly NotificationService _notificationService = NotificationService.Instance;

    public bool MinimizeToTray => _settings.MinimizeToTray;

    public MainWindowViewModel()
    {
        _settings = SettingsViewModel.Create();
        _statsService = SourceStatisticsService.Instance;

        var httpClient = new HttpClient();
        var logger = new SerilogLoggerAdapter<SourcePerformanceMonitor>();
        _performanceMonitor = new SourcePerformanceMonitor(httpClient, logger);
        _hostsService = new GithubHostsService();
        _hostsFileService = new WindowsHostsFileService();

        _historyService.OnOperationRecorded += OnOperationRecorded;
        _notificationService.OnNotification += OnNotificationReceived;

        ShowDashboard();
        InitializeSources();
        ApplySettings();
        StartAutoUpdate();
        CheckHostsStatus();

        // 自动启动监控 - 始终检测所有数据源状态
        _performanceMonitor.StartMonitoring();
        IsMonitoring = true;
        if (_settings.TestInterval > 0)
        {
            _autoUpdateTimer?.Start();
        }
        StatusMessage = "已启动自动检测，正在测试所有数据源...";

        IsDarkMode = ThemeManager.IsDarkMode;

        foreach (var record in _historyService.GetRecentRecords(20))
        {
            OperationHistory.Add(record);
        }
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
        IsDashboardVisible = true;
        IsSettingsVisible = false;
    }

    [RelayCommand]
    private void ShowSettings()
    {
        var settingsView = new SettingsView();
        settingsView.DataContext = _settings;
        CurrentView = settingsView;
        IsDashboardVisible = false;
        IsSettingsVisible = true;
        IsLogViewerVisible = false;
    }

    [RelayCommand]
    private void ShowLogViewer()
    {
        if (LogViewer == null)
        {
            LogViewer = new LogViewerViewModel();
        }
        
        var logViewerView = new GithubAccelerator.UI.Views.LogViewerView();
        logViewerView.DataContext = LogViewer;
        CurrentView = logViewerView;
        IsDashboardVisible = false;
        IsSettingsVisible = false;
        IsLogViewerVisible = true;
    }

    [RelayCommand]
    private void ShowBackupManager()
    {
        if (BackupManager == null)
        {
            BackupManager = new BackupManagerViewModel();
        }
        
        var backupManagerView = new GithubAccelerator.UI.Views.BackupManagerView();
        backupManagerView.DataContext = BackupManager;
        CurrentView = backupManagerView;
        IsDashboardVisible = false;
        IsSettingsVisible = false;
        IsLogViewerVisible = false;
        IsHostsGroupVisible = false;
    }

    [RelayCommand]
    private void ShowHostsGroupManager()
    {
        if (HostsGroupManager == null)
        {
            HostsGroupManager = new HostsGroupViewModel();
        }

        var hostsGroupView = new GithubAccelerator.UI.Views.HostsGroupView();
        hostsGroupView.DataContext = HostsGroupManager;
        CurrentView = hostsGroupView;
        IsDashboardVisible = false;
        IsSettingsVisible = false;
        IsLogViewerVisible = false;
        IsHostsGroupVisible = true;
        IsPerformanceChartVisible = false;
    }

    [RelayCommand]
    private void ShowPerformanceChart()
    {
        if (PerformanceChart == null)
        {
            PerformanceChart = new PerformanceChartViewModel();
            PerformanceChart.SetMonitor(_performanceMonitor);
        }

        var chartView = new GithubAccelerator.UI.Views.PerformanceChartView();
        chartView.DataContext = PerformanceChart;
        CurrentView = chartView;
        IsDashboardVisible = false;
        IsSettingsVisible = false;
        IsLogViewerVisible = false;
        IsHostsGroupVisible = false;
        IsPerformanceChartVisible = true;
        IsGitHubLatencyVisible = false;
    }

    [RelayCommand]
    private void ShowGitHubLatency()
    {
        if (GitHubLatency == null)
        {
            GitHubLatency = new GitHubLatencyViewModel();
        }

        var latencyView = new GithubAccelerator.UI.Views.GitHubLatencyView();
        latencyView.DataContext = GitHubLatency;
        CurrentView = latencyView;
        IsDashboardVisible = false;
        IsSettingsVisible = false;
        IsLogViewerVisible = false;
        IsHostsGroupVisible = false;
        IsPerformanceChartVisible = false;
        IsGitHubLatencyVisible = true;
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

    private void ApplySettings()
    {
        UpdateAutoUpdateTimer();
    }

    private void UpdateAutoUpdateTimer()
    {
        _autoUpdateTimer?.Stop();
        _autoUpdateTimer?.Dispose();
        
        _autoUpdateTimer = new System.Timers.Timer(_settings.TestInterval * 1000);
        _autoUpdateTimer.Elapsed += async (s, e) => await AutoUpdateAsync();
        if (IsMonitoring)
        {
            _autoUpdateTimer.Start();
        }
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
    }

    private async void OnSingleSourceApplyRequested(SourceStatusViewModel source)
    {
        try
        {
            StatusMessage = $"正在从 {source.Name} 获取 Hosts...";
            
            var httpClient = new HttpClient();
            var hostsContent = await httpClient.GetStringAsync(source.Url);
            
            var success = await _hostsFileService.ApplyGithubHostsAsync(hostsContent);
            
            if (success)
            {
                IsHostsApplied = true;
                CurrentHostsSource = source.Name;
                StatusMessage = $"Hosts 已成功应用，源：{source.Name}";
                _historyService.Record(OperationType.HostsApplied, $"从 {source.Name} 应用 Hosts 成功", true, $"响应时间: {source.ResponseTime}ms");
                _notificationService.Success("Hosts 应用", $"已从 {source.Name} 成功应用 Hosts");
            }
            else
            {
                StatusMessage = "Hosts 应用失败，请以管理员身份运行";
                _notificationService.Error("Hosts 应用", "应用失败，请以管理员身份运行");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"应用失败：{ex.Message}";
            _statsService.RecordTimeout(source.Name, source.Url);
            _notificationService.Error("Hosts 应用", $"应用失败：{ex.Message}");
        }
    }

    [RelayCommand]
    private async Task ClearAppliedHosts()
    {
        try
        {
            var currentContent = await _hostsFileService.ReadHostsFileAsync();
            const string startMarker = "# === GitHub Accelerator Start ===";
            const string endMarker = "# === GitHub Accelerator End ===";
            
            if (currentContent.Contains(startMarker))
            {
                await _hostsFileService.BackupHostsFileAsync();
                
                var startIndex = currentContent.IndexOf(startMarker, StringComparison.Ordinal);
                var endIndex = currentContent.IndexOf(endMarker, startIndex, StringComparison.Ordinal);
                
                if (endIndex >= 0)
                {
                    endIndex += endMarker.Length;
                    var cleanedContent = currentContent.Substring(0, startIndex) + currentContent.Substring(endIndex);
                    
                    var hostsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");
                    await File.WriteAllTextAsync(hostsPath, cleanedContent.Trim());
                    
                    IsHostsApplied = false;
                    CurrentHostsSource = string.Empty;
                    StatusMessage = "已清除我们添加的 Hosts 内容";
                    _historyService.Record(OperationType.HostsApplied, "清除 Hosts 成功", true);
                    _notificationService.Success("Hosts 清除", "已成功清除我们添加的 Hosts 内容");
                    return;
                }
            }
            
            StatusMessage = "没有找到需要清除的内容";
            _notificationService.Info("Hosts 清除", "当前没有我们添加的内容");
        }
        catch (Exception ex)
        {
            StatusMessage = $"清除失败：{ex.Message}";
            _notificationService.Error("Hosts 清除", $"清除失败：{ex.Message}");
        }
    }

    [RelayCommand]
    private async Task ToggleMonitoring()
    {
        if (IsMonitoring)
        {
            _performanceMonitor.StopMonitoring();
            IsMonitoring = false;
            _autoUpdateTimer?.Stop();
            StatusMessage = "监控已停止";
            _historyService.Record(OperationType.MonitoringStopped, "停止监控");
        }
        else
        {
            _performanceMonitor.StartMonitoring();
            IsMonitoring = true;
            if (_settings.TestInterval > 0)
            {
                _autoUpdateTimer?.Start();
            }
            StatusMessage = "监控已启动，正在测试所有数据源...";
            _historyService.Record(OperationType.MonitoringStarted, "启动监控");
            _notificationService.Info("监控", "监控已启动，正在测试所有数据源");
        }
    }

    [RelayCommand]
    private async Task RefreshSources()
    {
        StatusMessage = "正在刷新数据源状态...";
        var metrics = await _performanceMonitor.TestAllSourcesAsync();
        
        for (int i = 0; i < metrics.Length && i < Sources.Count; i++)
        {
            Sources[i].UpdateFromMetrics(metrics[i]);
        }
        
        UpdateBestSource();
        StatusMessage = $"已刷新 {metrics.Length} 个数据源状态";
        _historyService.Record(OperationType.SourcesRefreshed, $"刷新了 {metrics.Length} 个数据源");
    }

    [RelayCommand]
    private async Task ApplyHosts()
    {
        try
        {
            // 确定要使用的数据源
            SourcePerformanceMetrics? best = null;
            
            // 首先尝试获取推荐的源
            best = _performanceMonitor.GetBestSource();
            
            // 如果没有推荐的源，使用响应时间最好的源
            if (best == null || best.AverageResponseTimeMs <= 0)
            {
                var allSources = _performanceMonitor.GetAllMetrics()
                    .Where(m => m.AverageResponseTimeMs > 0)
                    .OrderBy(m => m.AverageResponseTimeMs);
                
                best = allSources.FirstOrDefault();
                
                if (best == null)
                {
                    // 如果还是没有可用的源，直接使用第一个数据源的URL
                    if (Sources.Count > 0)
                    {
                        var firstSource = Sources.FirstOrDefault();
                        await ApplyHostsFromSource(firstSource.Name, firstSource.Url);
                        return;
                    }
                    
                    StatusMessage = "错误：没有可用的数据源";
                    _historyService.Record(OperationType.HostsApplied, "应用 Hosts 失败：没有可用数据源", false);
                    _notificationService.Error("Hosts 应用", "没有可用的数据源");
                    return;
                }
            }

            await ApplyHostsFromSource(best.Name, best.Url);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Hosts 应用失败：{ex.Message}";
            _historyService.Record(OperationType.HostsApplied, $"应用 Hosts 异常：{ex.Message}", false, ex.StackTrace);
            _notificationService.Error("Hosts 应用", $"应用失败：{ex.Message}");
        }
    }
    
    private async Task ApplyHostsFromSource(string sourceName, string sourceUrl)
    {
        StatusMessage = $"正在从 {sourceName} 获取 Hosts...";
        
        try
        {
            var httpClient = new HttpClient();
            var hostsContent = await httpClient.GetStringAsync(sourceUrl);
            
            var success = await _hostsFileService.ApplyGithubHostsAsync(hostsContent);
            
            if (success)
            {
                IsHostsApplied = true;
                CurrentHostsSource = sourceName;
                StatusMessage = $"Hosts 已成功应用，源：{sourceName}";
                _historyService.Record(OperationType.HostsApplied, $"从 {sourceName} 应用 Hosts 成功", true);
                _statsService.RecordSourceUsed(sourceName, sourceUrl, 0, true);
                _notificationService.Success("Hosts 应用", $"已从 {sourceName} 成功应用 Hosts");
            }
            else
            {
                StatusMessage = "Hosts 应用失败，请以管理员身份运行";
                _historyService.Record(OperationType.HostsApplied, "应用 Hosts 失败：权限不足", false);
                _notificationService.Error("Hosts 应用", "应用失败，请以管理员身份运行");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Hosts 应用失败：{ex.Message}";
            _historyService.Record(OperationType.HostsApplied, $"应用 Hosts 异常：{ex.Message}", false, ex.StackTrace);
            _statsService.RecordTimeout(sourceName, sourceUrl);
            _notificationService.Error("Hosts 应用", $"应用失败：{ex.Message}");
        }
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

        try
        {
            var httpClient = new HttpClient();
            var allHostsContent = new System.Text.StringBuilder();
            allHostsContent.AppendLine("# GitHub Accelerator - Applied Sources");
            allHostsContent.AppendLine($"# Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            allHostsContent.AppendLine();

            foreach (var source in selectedSources)
            {
                StatusMessage = $"正在从 {source.Name} 获取 Hosts...";
                try
                {
                    var content = await httpClient.GetStringAsync(source.Url);
                    allHostsContent.AppendLine($"# Source: {source.Name}");
                    allHostsContent.AppendLine(content);
                    allHostsContent.AppendLine();
                }
                catch (Exception ex)
                {
                    _historyService.Record(OperationType.HostsApplied, $"从 {source.Name} 获取失败：{ex.Message}", false);
                }
            }

            var success = await _hostsFileService.ApplyGithubHostsAsync(allHostsContent.ToString());
            
            if (success)
            {
                IsHostsApplied = true;
                CurrentHostsSource = $"{selectedSources.Count} 个源";
                StatusMessage = $"已成功应用 {selectedSources.Count} 个数据源";
                _historyService.Record(OperationType.HostsApplied, $"应用 {selectedSources.Count} 个数据源成功", true);
                _notificationService.Success("Hosts 应用", $"已成功应用 {selectedSources.Count} 个数据源");
            }
            else
            {
                StatusMessage = "Hosts 应用失败，请以管理员身份运行";
                _notificationService.Error("Hosts 应用", "应用失败，请以管理员身份运行");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"应用失败：{ex.Message}";
            _notificationService.Error("Hosts 应用", $"应用失败：{ex.Message}");
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
        var best = _performanceMonitor.GetBestSource();
        if (best != null && best.AverageResponseTimeMs > 0)
        {
            BestSourceName = best.Name;
            BestSourceResponseTime = (long)best.AverageResponseTimeMs;
            BestSourceScore = best.OverallScore;
        }
    }

    private void StartAutoUpdate()
    {
        _updateTimer = new System.Timers.Timer(5000);
        _updateTimer.Elapsed += async (s, e) => await AutoUpdateAsync();
        _updateTimer.Start();
    }

    private async Task AutoUpdateAsync()
    {
        if (!IsMonitoring) return;

        try
        {
            var metrics = _performanceMonitor.GetCurrentMetrics();
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                for (int i = 0; i < metrics.Length && i < Sources.Count; i++)
                {
                    Sources[i].UpdateFromMetrics(metrics[i]);
                }
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
        catch
        {
            // 忽略自动更新错误
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
        catch
        {
            // 忽略错误
        }
    }

    [RelayCommand]
    private async Task ViewHostsContent()
    {
        try
        {
            var hostsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");
            HostsContent = await File.ReadAllTextAsync(hostsPath);
            
            var hostsContentView = new GithubAccelerator.UI.Views.HostsContentView();
            hostsContentView.DataContext = this;
            CurrentView = hostsContentView;
            IsDashboardVisible = false;
            IsSettingsVisible = false;
            IsLogViewerVisible = false;
            IsHostsGroupVisible = false;
            IsPerformanceChartVisible = false;
            IsGitHubLatencyVisible = false;
            
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

    [RelayCommand]
    private async Task ExportDataAsync()
    {
        try
        {
            var window = App.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

            if (window == null) return;

            var storageProvider = window.StorageProvider;
            var file = await storageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = "导出数据",
                SuggestedFileName = $"GithubAccelerator_backup_{DateTime.Now:yyyyMMdd_HHmmss}.zip",
                FileTypeChoices = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("压缩包")
                    {
                        Patterns = new[] { "*.zip" }
                    },
                    new Avalonia.Platform.Storage.FilePickerFileType("JSON 文件")
                    {
                        Patterns = new[] { "*.json" }
                    }
                }
            });

            if (file == null) return;

            var filePath = file.TryGetLocalPath();
            if (string.IsNullOrEmpty(filePath)) return;

            StatusMessage = "正在导出数据...";
            var exportService = DataExportImportService.Instance;
            exportService.OnExportProgress += msg => StatusMessage = msg;

            var success = await exportService.ExportAsync(filePath);
            if (success)
            {
                StatusMessage = $"数据已导出到：{Path.GetFileName(filePath)}";
                _historyService.Record(OperationType.SettingsChanged, $"导出数据到 {Path.GetFileName(filePath)}");
                _notificationService.Success("数据导出", "数据已成功导出");
            }
            else
            {
                StatusMessage = "导出失败";
                _notificationService.Error("数据导出", "导出失败");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"导出异常：{ex.Message}";
            _notificationService.Error("数据导出", $"导出异常：{ex.Message}");
        }
    }

    [RelayCommand]
    private async Task ImportDataAsync()
    {
        try
        {
            var window = App.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

            if (window == null) return;

            var storageProvider = window.StorageProvider;
            var files = await storageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "导入数据",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("数据文件")
                    {
                        Patterns = new[] { "*.zip", "*.json" }
                    }
                }
            });

            if (files.Count == 0) return;

            var filePath = files[0].TryGetLocalPath();
            if (string.IsNullOrEmpty(filePath)) return;

            StatusMessage = "正在导入数据...";
            var importService = DataExportImportService.Instance;
            importService.OnImportProgress += msg => StatusMessage = msg;

            var data = await importService.ImportAsync(filePath);
            if (data == null)
            {
                StatusMessage = "导入失败：无效的数据文件";
                _notificationService.Error("数据导入", "无效的数据文件");
                return;
            }

            var success = await importService.ApplyImportedDataAsync(data);
            if (success)
            {
                StatusMessage = "数据已成功导入";
                _historyService.Record(OperationType.SettingsChanged, "导入数据成功");
                _notificationService.Success("数据导入", "数据已成功导入");
            }
            else
            {
                StatusMessage = "导入失败";
                _notificationService.Error("数据导入", "导入失败");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"导入异常：{ex.Message}";
            _notificationService.Error("数据导入", $"导入异常：{ex.Message}");
        }
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
