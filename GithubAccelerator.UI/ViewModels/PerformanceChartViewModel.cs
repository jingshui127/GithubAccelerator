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
    ResponseTimeHistory,
    SourceComparison,
    ScoreDistribution
}

public partial class PerformanceChartViewModel : ObservableObject
{
    private readonly PerformanceChartService _chartService;
    private readonly OperationHistoryService _historyService;
    private ISourcePerformanceMonitor? _monitor;

    [ObservableProperty]
    private ChartType _selectedChartType = ChartType.ResponseTimeHistory;

    [ObservableProperty]
    private int _selectedSourceIndex = 0;

    [ObservableProperty]
    private string[] _sourceNames = Array.Empty<string>();

    [ObservableProperty]
    private string _statusMessage = "就绪";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private double _chartMinValue;

    [ObservableProperty]
    private double _chartMaxValue = 100;

    [ObservableProperty]
    private string _chartTitle = "响应时间趋势";

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
        UpdateSourceNames();
        LoadChartData();
    }

    partial void OnSelectedChartTypeChanged(ChartType value)
    {
        LoadChartData();
    }

    partial void OnSelectedSourceIndexChanged(int value)
    {
        if (SelectedChartType == ChartType.ScoreDistribution)
        {
            LoadChartData();
        }
    }

    private void UpdateSourceNames()
    {
        if (_monitor == null) return;
        var metrics = _monitor.GetCurrentMetrics();
        SourceNames = metrics.Select(m => m.Name).ToArray();
    }

    private void LoadChartData()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            try
            {
                IsLoading = true;

                List<ChartSeries> series;
                switch (SelectedChartType)
                {
                    case ChartType.ResponseTimeHistory:
                        series = _chartService.GetResponseTimeHistory(50);
                        ChartTitle = "响应时间趋势";
                        ChartUnit = "ms";
                        YAxisName = "响应时间 (ms)";
                        IsLineChart = true;
                        IsBarChart = false;
                        if (series.Count > 0 && series[0].DataPoints.Count > 0)
                        {
                            var allValues = series.SelectMany(s => s.DataPoints.Select(d => d.Value)).ToList();
                            ChartMinValue = Math.Floor(allValues.Min() / 100) * 100;
                            ChartMaxValue = Math.Ceiling(allValues.Max() / 100) * 100;
                            if (ChartMinValue == ChartMaxValue) ChartMaxValue += 100;
                            ChartMaxY = ChartMaxValue;
                            LineChartValues = allValues;
                        }
                        else
                        {
                            ChartMinValue = 0;
                            ChartMaxValue = 1000;
                            ChartMaxY = 1000;
                            LineChartValues = new List<double>();
                        }
                        break;

                    case ChartType.SourceComparison:
                        series = _chartService.GetSuccessRateComparison();
                        ChartTitle = "数据源对比";
                        ChartUnit = "%";
                        YAxisName = "成功率 (%)";
                        ChartMinValue = 0;
                        ChartMaxValue = 120;
                        ChartMaxY = 120;
                        IsLineChart = false;
                        IsBarChart = true;
                        var barItems = new List<BarItem>();
                        foreach (var s in series)
                        {
                            if (s.DataPoints.Count > 0)
                            {
                                barItems.Add(new BarItem
                                {
                                    Label = s.Name.Length > 10 ? s.Name.Substring(0, 10) + "..." : s.Name,
                                    Value = s.DataPoints.Average(d => d.Value)
                                });
                            }
                        }
                        BarItems = barItems;
                        break;

                    case ChartType.ScoreDistribution:
                        series = _chartService.GetScoreDistribution(SelectedSourceIndex);
                        var metrics = _monitor?.GetCurrentMetrics();
                        if (metrics != null && SelectedSourceIndex < metrics.Length)
                        {
                            ChartTitle = $"{metrics[SelectedSourceIndex].Name} 评分趋势";
                        }
                        else
                        {
                            ChartTitle = "评分趋势";
                        }
                        ChartUnit = "分";
                        YAxisName = "评分";
                        ChartMinValue = 0;
                        ChartMaxValue = 100;
                        ChartMaxY = 100;
                        IsLineChart = true;
                        IsBarChart = false;
                        if (series.Count > 0 && series[0].DataPoints.Count > 0)
                        {
                            LineChartValues = series[0].DataPoints.Select(d => d.Value).ToList();
                        }
                        else
                        {
                            LineChartValues = new List<double>();
                        }
                        break;

                    default:
                        series = new List<ChartSeries>();
                        LineChartValues = new List<double>();
                        BarItems = new List<BarItem>();
                        break;
                }

                HasNoData = (IsLineChart && LineChartValues.Count == 0) ||
                            (IsBarChart && BarItems.Count == 0);

                StatusMessage = $"已加载 {series.Count} 个数据系列，共 {series.Sum(s => s.DataPoints.Count)} 个数据点";
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

    [RelayCommand]
    private void SwitchToResponseTime()
    {
        SelectedChartType = ChartType.ResponseTimeHistory;
    }

    [RelayCommand]
    private void SwitchToComparison()
    {
        SelectedChartType = ChartType.SourceComparison;
    }

    [RelayCommand]
    private void SwitchToScoreDistribution()
    {
        SelectedChartType = ChartType.ScoreDistribution;
    }
}
