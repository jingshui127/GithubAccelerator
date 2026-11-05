using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GithubAccelerator.UI.Services;

namespace GithubAccelerator.UI.ViewModels
{
    public partial class GitHubLatencyViewModel : ObservableObject, IDisposable
    {
        private readonly GitHubLatencyMonitorService _monitorService = GitHubLatencyMonitorService.Instance;
        private readonly OperationHistoryService _historyService = OperationHistoryService.Instance;
        private bool _isInitialized = false;

        // Chart Y axis 相关常量
        private const double ChartYAxisStep = 100;
        private const double MinChartMaxY = 200;
        private const double DefaultChartMaxY = 1000;

        // 延迟状态判定字符串常量
        private const string StatusNotMonitoring = "未监控";
        private const string StatusDetecting = "检测中";
        private const string StatusExcellent = "优秀";
        private const string StatusGood = "良好";
        private const string StatusAverage = "一般";
        private const string StatusPoor = "较差";

        [ObservableProperty]
        private ObservableCollection<GitHubLatencyRecord> _latencyRecords = new();

        [ObservableProperty]
        private int _currentLatency;

        [ObservableProperty]
        private int _averageLatency;

        [ObservableProperty]
        private int _minLatency;

        [ObservableProperty]
        private int _maxLatency;

        [ObservableProperty]
        private double _successRate;

        [ObservableProperty]
        private bool _isMonitoring;

        [ObservableProperty]
        private string _statusMessage = "未启动监控";

        [ObservableProperty]
        private string _latencyStatus = "正常";

        [ObservableProperty]
        private IList<double> _chartValues = new List<double>();

        [ObservableProperty]
        private double _chartMaxY = 1000;

        public GitHubLatencyViewModel()
        {
            try
            {
                _monitorService.OnLatencyUpdated += OnLatencyUpdated;

                // 注意：必须先置位 _isInitialized，再调用 UpdateFromService，
                // 否则 UpdateFromService 内部的早返回判断会让此次刷新失效。
                _isInitialized = true;
                UpdateFromService();

                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        _monitorService.StartMonitoring();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"启动监控失败: {ex}");
                    }
                });
            }
            catch (Exception ex)
            {
                StatusMessage = $"初始化失败: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"GitHubLatencyViewModel 初始化失败: {ex}");
            }
        }

        private void OnLatencyUpdated()
        {
            try
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        UpdateFromService();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"UpdateFromService 失败: {ex}");
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OnLatencyUpdated 失败: {ex}");
            }
        }

        private void UpdateFromService()
        {
            if (!_isInitialized) return;
            
            CurrentLatency = _monitorService.CurrentLatency;
            AverageLatency = _monitorService.AverageLatency;
            MinLatency = _monitorService.MinLatency;
            MaxLatency = _monitorService.MaxLatency;
            SuccessRate = _monitorService.SuccessRate;
            IsMonitoring = _monitorService.IsMonitoring;
            
            var recentRecords = _monitorService.GetRecentRecords(30);

            // 注意：这里采用 Clear + Add 的整体重建模式，会带来轻微的 UI 闪烁。
            // 增量更新（diff/patch）逻辑较复杂，且本场景下记录数量有限（最多 30 条），
            // 暂时保留该简单实现，后续若有性能或视觉问题再考虑增量更新。
            LatencyRecords.Clear();
            var latencyValues = new List<double>();
            foreach (var record in recentRecords)
            {
                LatencyRecords.Add(record);
                if (record.LatencyMs > 0)
                {
                    latencyValues.Add(Math.Min(record.LatencyMs, 5000));
                }
            }

            ChartValues = latencyValues;

            if (latencyValues.Count > 0)
            {
                var maxVal = latencyValues.Max();
                ChartMaxY = Math.Ceiling(maxVal / ChartYAxisStep) * ChartYAxisStep + ChartYAxisStep;
                if (ChartMaxY < MinChartMaxY) ChartMaxY = MinChartMaxY;
            }
            else
            {
                ChartMaxY = DefaultChartMaxY;
            }

            if (!IsMonitoring)
            {
                StatusMessage = "监控已停止";
                LatencyStatus = StatusNotMonitoring;
            }
            else if (CurrentLatency == 0)
            {
                StatusMessage = "等待数据...";
                LatencyStatus = StatusDetecting;
            }
            else if (CurrentLatency < 100)
            {
                StatusMessage = $"最后更新：{DateTime.Now:HH:mm:ss}";
                LatencyStatus = StatusExcellent;
            }
            else if (CurrentLatency < 300)
            {
                StatusMessage = $"最后更新：{DateTime.Now:HH:mm:ss}";
                LatencyStatus = StatusGood;
            }
            else if (CurrentLatency < 500)
            {
                StatusMessage = $"最后更新：{DateTime.Now:HH:mm:ss}";
                LatencyStatus = StatusAverage;
            }
            else
            {
                StatusMessage = $"最后更新：{DateTime.Now:HH:mm:ss}";
                LatencyStatus = StatusPoor;
            }
        }

        [RelayCommand]
        private void StartMonitoring()
        {
            _monitorService.StartMonitoring();
            _historyService.Record(OperationType.SettingsChanged, "启动 GitHub 延迟监控");
            UpdateFromService();
        }

        [RelayCommand]
        private void StopMonitoring()
        {
            _monitorService.StopMonitoring();
            _historyService.Record(OperationType.SettingsChanged, "停止 GitHub 延迟监控");
            UpdateFromService();
        }

        [RelayCommand]
        private void ClearData()
        {
            _monitorService.ClearRecords();
            LatencyRecords.Clear();
            ChartValues = new List<double>();
            _historyService.Record(OperationType.SettingsChanged, "清除 GitHub 延迟历史数据");
            UpdateFromService();
        }

        public void Dispose()
        {
            if (_monitorService != null)
            {
                _monitorService.OnLatencyUpdated -= OnLatencyUpdated;
            }
        }
    }
}
