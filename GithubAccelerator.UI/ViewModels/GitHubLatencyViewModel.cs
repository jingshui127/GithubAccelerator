using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GithubAccelerator.UI.Services;

namespace GithubAccelerator.UI.ViewModels
{
    public partial class GitHubLatencyViewModel : ObservableObject
    {
        private readonly GitHubLatencyMonitorService _monitorService = GitHubLatencyMonitorService.Instance;
        private readonly OperationHistoryService _historyService = OperationHistoryService.Instance;
        private bool _isInitialized = false;

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
                _monitorService = GitHubLatencyMonitorService.Instance;
                _historyService = OperationHistoryService.Instance;
                
                _monitorService.OnLatencyUpdated += OnLatencyUpdated;
                
                UpdateFromService();
                _isInitialized = true;
                
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
                ChartMaxY = Math.Ceiling(maxVal / 100) * 100 + 100;
                if (ChartMaxY < 200) ChartMaxY = 200;
            }
            else
            {
                ChartMaxY = 1000;
            }

            if (!IsMonitoring)
            {
                StatusMessage = "监控已停止";
                LatencyStatus = "未监控";
            }
            else if (CurrentLatency == 0)
            {
                StatusMessage = "等待数据...";
                LatencyStatus = "检测中";
            }
            else if (CurrentLatency < 100)
            {
                StatusMessage = $"最后更新：{DateTime.Now:HH:mm:ss}";
                LatencyStatus = "优秀";
            }
            else if (CurrentLatency < 300)
            {
                StatusMessage = $"最后更新：{DateTime.Now:HH:mm:ss}";
                LatencyStatus = "良好";
            }
            else if (CurrentLatency < 500)
            {
                StatusMessage = $"最后更新：{DateTime.Now:HH:mm:ss}";
                LatencyStatus = "一般";
            }
            else
            {
                StatusMessage = $"最后更新：{DateTime.Now:HH:mm:ss}";
                LatencyStatus = "较差";
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
