using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using GithubAccelerator.Services;

namespace GithubAccelerator.UI.Services;

public class ChartDataPoint
{
    public DateTime Time { get; set; }
    public double Value { get; set; }
    public string Label { get; set; } = string.Empty;
}

public class ChartSeries
{
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#2196F3";
    public ObservableCollection<ChartDataPoint> DataPoints { get; set; } = new();
}

public class PerformanceChartService
{
    private static readonly Lazy<PerformanceChartService> _instance = new(() => new PerformanceChartService());
    public static PerformanceChartService Instance => _instance.Value;

    private ISourcePerformanceMonitor? _monitor;
    private readonly List<SourceTestRecord> _cachedRecords = new();
    private DateTime _lastCacheUpdate = DateTime.MinValue;
    private readonly TimeSpan _cacheExpiry = TimeSpan.FromSeconds(10);

    public event Action? OnDataChanged;

    public void SetMonitor(ISourcePerformanceMonitor monitor)
    {
        _monitor = monitor;
    }

    public List<ChartSeries> GetResponseTimeHistory(int maxPoints = 50)
    {
        var seriesList = new List<ChartSeries>();
        if (_monitor == null) return seriesList;

        var metrics = _monitor.GetCurrentMetrics();
        if (metrics == null || metrics.Length == 0) return seriesList;

        var colors = new[] { "#2196F3", "#4CAF50", "#FF9800", "#E91E63", "#9C27B0", "#00BCD4" };

        foreach (var metric in metrics)
        {
            var history = _monitor.GetHistory(metric.Url);
            if (history == null) continue;

            var recentRecords = history.GetRecentRecords(maxPoints);
            if (recentRecords.Count == 0) continue;

            var series = new ChartSeries
            {
                Name = metric.Name,
                Color = colors[Array.IndexOf(metrics, metric) % colors.Length]
            };

            foreach (var record in recentRecords.OrderBy(r => r.TestTime))
            {
                series.DataPoints.Add(new ChartDataPoint
                {
                    Time = record.TestTime,
                    Value = record.ResponseTimeMs,
                    Label = $"{record.ResponseTimeMs}ms"
                });
            }

            seriesList.Add(series);
        }

        return seriesList;
    }

    public List<ChartSeries> GetSuccessRateComparison()
    {
        var seriesList = new List<ChartSeries>();
        if (_monitor == null) return seriesList;

        var metrics = _monitor.GetCurrentMetrics().OrderByDescending(m => m.OverallScore).ToArray();

        var series = new ChartSeries
        {
            Name = "综合评分",
            Color = "#4CAF50"
        };

        var successSeries = new ChartSeries
        {
            Name = "成功率(%)",
            Color = "#2196F3"
        };

        var responseSeries = new ChartSeries
        {
            Name = "响应时间(ms)",
            Color = "#FF9800"
        };

        foreach (var metric in metrics)
        {
            var time = DateTime.Now;
            series.DataPoints.Add(new ChartDataPoint { Time = time, Value = metric.OverallScore, Label = metric.Name });
            successSeries.DataPoints.Add(new ChartDataPoint { Time = time, Value = metric.SuccessRate * 100, Label = metric.Name });
            responseSeries.DataPoints.Add(new ChartDataPoint { Time = time, Value = metric.AverageResponseTimeMs, Label = metric.Name });
        }

        seriesList.Add(series);
        seriesList.Add(successSeries);
        seriesList.Add(responseSeries);

        return seriesList;
    }

    public List<ChartSeries> GetScoreDistribution(int sourceIndex = 0)
    {
        var seriesList = new List<ChartSeries>();
        if (_monitor == null) return seriesList;

        var metrics = _monitor.GetCurrentMetrics();
        if (metrics == null || metrics.Length == 0 || sourceIndex >= metrics.Length) return seriesList;

        var metric = metrics[sourceIndex];
        var history = _monitor.GetHistory(metric.Url);
        if (history == null) return seriesList;

        var scoreSeries = new ChartSeries
        {
            Name = $"{metric.Name} 综合评分趋势",
            Color = "#2196F3"
        };

        var stabilitySeries = new ChartSeries
        {
            Name = "稳定性评分",
            Color = "#4CAF50"
        };

        var integritySeries = new ChartSeries
        {
            Name = "完整性评分",
            Color = "#FF9800"
        };

        foreach (var m in history.MetricsHistory.TakeLast(30))
        {
            scoreSeries.DataPoints.Add(new ChartDataPoint { Time = m.LastTestTime, Value = m.OverallScore });
            stabilitySeries.DataPoints.Add(new ChartDataPoint { Time = m.LastTestTime, Value = m.StabilityScore });
            integritySeries.DataPoints.Add(new ChartDataPoint { Time = m.LastTestTime, Value = m.DataIntegrityScore });
        }

        seriesList.Add(scoreSeries);
        seriesList.Add(stabilitySeries);
        seriesList.Add(integritySeries);

        return seriesList;
    }

    public Dictionary<string, double> GetSourceSummary()
    {
        var summary = new Dictionary<string, double>();
        if (_monitor == null) return summary;

        var metrics = _monitor.GetCurrentMetrics();
        foreach (var metric in metrics)
        {
            summary[metric.Name] = metric.OverallScore;
        }

        return summary;
    }

    public (double Min, double Max, double Avg) GetResponseTimeStats(string sourceUrl)
    {
        if (_monitor == null) return (0, 0, 0);

        var history = _monitor.GetHistory(sourceUrl);
        if (history == null) return (0, 0, 0);

        var records = history.GetRecentRecords(100).Where(r => r.IsSuccess).ToList();
        if (records.Count == 0) return (0, 0, 0);

        var times = records.Select(r => (double)r.ResponseTimeMs).ToArray();
        return (times.Min(), times.Max(), times.Average());
    }

    public void RefreshData()
    {
        OnDataChanged?.Invoke();
    }
}
