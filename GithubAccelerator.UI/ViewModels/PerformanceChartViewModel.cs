using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GithubAccelerator.Services;
using GithubAccelerator.UI.Controls;
using GithubAccelerator.UI.Services;

namespace GithubAccelerator.UI.ViewModels;

public enum ChartType
{
    Overview // 综合视图
}

public partial class PerformanceChartViewModel : ObservableObject
{
    private readonly PerformanceChartService _chartService;
    private readonly OperationHistoryService _historyService;
    private ISourcePerformanceMonitor? _monitor;

    [ObservableProperty]
    private ChartType _selectedChartType = ChartType.Overview;

    [ObservableProperty]
    private string _statusMessage = "就绪";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private double _chartMinValue;

    [ObservableProperty]
    private double _chartMaxValue = 100;

    [ObservableProperty]
    private string _chartTitle = "网络性能概览";

    [ObservableProperty]
    private string _chartUnit = "ms";

    [ObservableProperty]
    private string _yAxisName = "响应时间 (ms)";

    [ObservableProperty]
    private IList<double> _lineChartValues = new List<double>();

    [ObservableProperty]
    private IList<BarItem> _barItems = new List<BarItem>();

    [ObservableProperty]
    private double _chartMaxY = 1000;

    [ObservableProperty]
    private bool _isLineChart = true;

    [ObservableProperty]
    private bool _isBarChart;

    [ObservableProperty]
    private bool _hasNoData = true;

    [ObservableProperty]
    private List<SourcePerformanceMetrics> _currentMetrics = new();

    [ObservableProperty]
    private double _bestResponseTime = 0;

    [ObservableProperty]
    private string _bestSourceName = "";

    [ObservableProperty]
    private double _averageSuccessRate = 0;

    public PerformanceChartViewModel()
    {
        _chartService = PerformanceChartService.Instance;
        _historyService = OperationHistoryService.Instance;
        LoadChartData();
        _chartService.OnDataChanged += OnDataChanged;
    }

    public void SetMonitor(ISourcePerformanceMonitor monitor)
    {
        _monitor = monitor;
        _chartService.SetMonitor(monitor);
        LoadChartData();
    }

    private void LoadChartData()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            try
            {
                IsLoading = true;

                // 综合视图：显示响应时间趋势
                var responseTimeSeries = _chartService.GetResponseTimeHistory(30);

                // 更新响应时间趋势
                ChartTitle = "网络性能概览";
                ChartUnit = "ms";
                YAxisName = "响应时间 (ms)";
                IsLineChart = true;
                IsBarChart = false;

                if (responseTimeSeries.Count > 0 && responseTimeSeries[0].DataPoints.Count > 0)
                {
                    var allValues = responseTimeSeries.SelectMany(s => s.DataPoints.Select(d => d.Value)).ToList();
                    ChartMinValue = 0;
                    ChartMaxValue = Math.Ceiling(allValues.Max() / 100) * 100;
                    if (ChartMaxValue < 500) ChartMaxValue = 500;
                    ChartMaxY = ChartMaxValue;
                    LineChartValues = allValues;
                    HasNoData = false;
                }
                else
                {
                    ChartMinValue = 0;
                    ChartMaxValue = 500;
                    ChartMaxY = 500;
                    LineChartValues = new List<double>();
                    HasNoData = true;
                }

                // 更新最佳数据源信息
                UpdateBestSourceInfo();

                // 更新平均成功率
                UpdateAverageSuccessRate();

                StatusMessage = "数据已更新";
            }
            catch (Exception ex)
            {
                StatusMessage = $"加载数据失败：{ex.Message}";
                HasNoData = true;
            }
            finally
            {
                IsLoading = false;
            }
        });
    }

    private void UpdateBestSourceInfo()
    {
        if (_monitor == null) return;
        
        var bestSource = _monitor.GetBestSource();
        if (bestSource != null)
        {
            BestResponseTime = bestSource.AverageResponseTimeMs;
            BestSourceName = bestSource.Name;
        }
    }

    private void UpdateAverageSuccessRate()
    {
        if (_monitor == null) return;

        var metrics = _monitor.GetCurrentMetrics();
        if (metrics.Length > 0)
        {
            AverageSuccessRate = metrics.Average(m => m.SuccessRate) * 100;
        }
    }

    private void OnDataChanged()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(LoadChartData);
    }

    [RelayCommand]
    private void RefreshData()
    {
        _historyService.Record(OperationType.SettingsChanged, "刷新性能图表数据");
        LoadChartData();
    }
}
