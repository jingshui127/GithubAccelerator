using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Linq;

namespace GithubAccelerator.UI.Services
{
    public class SourceStatisticsService
    {
        private static readonly Lazy<SourceStatisticsService> _instance = new(() => new SourceStatisticsService());
        public static SourceStatisticsService Instance => _instance.Value;

        private readonly string _statsFilePath;
        private Dictionary<string, SourceStatistics> _statistics = new();
        private readonly object _lock = new();

        public SourceStatisticsService()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GithubAccelerator");
            Directory.CreateDirectory(appDataPath);
            _statsFilePath = Path.Combine(appDataPath, "source_statistics.json");
            LoadStatistics();
        }

        public void RecordSourceUsed(string sourceName, string sourceUrl, long responseTimeMs, bool success)
        {
            lock (_lock)
            {
                if (!_statistics.ContainsKey(sourceUrl))
                {
                    _statistics[sourceUrl] = new SourceStatistics
                    {
                        SourceName = sourceName,
                        SourceUrl = sourceUrl
                    };
                }

                var stats = _statistics[sourceUrl];
                stats.TotalUsageCount++;
                stats.LastUsedTime = DateTime.Now;

                if (success)
                {
                    stats.SuccessCount++;
                    if (responseTimeMs > 0)
                    {
                        stats.TotalResponseTime += responseTimeMs;
                        stats.AverageResponseTime = stats.TotalResponseTime / stats.SuccessCount;
                        
                        if (responseTimeMs < stats.BestResponseTime || stats.BestResponseTime == 0)
                            stats.BestResponseTime = responseTimeMs;
                    }
                }
                else
                {
                    stats.FailureCount++;
                }

                stats.UpdateHealthScore();
                SaveStatistics();
            }
        }

        public void RecordTimeout(string sourceName, string sourceUrl)
        {
            lock (_lock)
            {
                if (!_statistics.ContainsKey(sourceUrl))
                {
                    _statistics[sourceUrl] = new SourceStatistics
                    {
                        SourceName = sourceName,
                        SourceUrl = sourceUrl
                    };
                }

                var stats = _statistics[sourceUrl];
                stats.TimeoutCount++;
                stats.TotalUsageCount++;
                stats.LastUsedTime = DateTime.Now;
                stats.UpdateHealthScore();
                SaveStatistics();
            }
        }

        public Dictionary<string, SourceStatistics> GetAllStatistics()
        {
            lock (_lock)
            {
                return new Dictionary<string, SourceStatistics>(_statistics);
            }
        }

        public SourceStatistics? GetStatistics(string sourceUrl)
        {
            lock (_lock)
            {
                return _statistics.ContainsKey(sourceUrl) ? _statistics[sourceUrl] : null;
            }
        }

        public SourceStatistics[] GetStatisticsOrderedByHealth()
        {
            lock (_lock)
            {
                return _statistics.Values
                    .OrderByDescending(s => s.HealthScore)
                    .ThenBy(s => s.TimeoutCount)
                    .ToArray();
            }
        }

        public SourceStatistics[] GetLeastHealthySources(int count = 3)
        {
            lock (_lock)
            {
                return _statistics.Values
                    .OrderBy(s => s.HealthScore)
                    .ThenByDescending(s => s.TimeoutCount)
                    .Take(count)
                    .ToArray();
            }
        }

        public string GetStatisticsReport()
        {
            lock (_lock)
            {
                if (_statistics.Count == 0)
                    return "暂无统计数据";

                var report = "=== 数据源使用统计报告 ===\n\n";
                
                var orderedStats = _statistics.Values.OrderByDescending(s => s.TotalUsageCount).ToList();
                
                foreach (var stats in orderedStats)
                {
                    report += $"数据源: {stats.SourceName}\n";
                    report += $"  使用次数: {stats.TotalUsageCount}\n";
                    report += $"  成功次数: {stats.SuccessCount}\n";
                    report += $"  失败次数: {stats.FailureCount}\n";
                    report += $"  超时次数: {stats.TimeoutCount}\n";
                    report += $"  平均响应: {stats.AverageResponseTime:F0}ms\n";
                    report += $"  最佳响应: {stats.BestResponseTime}ms\n";
                    report += $"  健康评分: {stats.HealthScore:F1}\n";
                    report += $"  最后使用: {stats.LastUsedTime:yyyy-MM-dd HH:mm}\n";
                    report += "\n";
                }

                var unhealthy = GetLeastHealthySources();
                if (unhealthy.Length > 0)
                {
                    report += "=== 建议替换的数据源 ===\n";
                    foreach (var s in unhealthy)
                    {
                        report += $"- {s.SourceName}: 健康评分 {s.HealthScore:F1}, 超时 {s.TimeoutCount} 次\n";
                    }
                }

                return report;
            }
        }

        public void ClearStatistics()
        {
            lock (_lock)
            {
                _statistics.Clear();
                SaveStatistics();
            }
        }

        private void LoadStatistics()
        {
            try
            {
                if (File.Exists(_statsFilePath))
                {
                    var json = File.ReadAllText(_statsFilePath);
                    var data = JsonSerializer.Deserialize<Dictionary<string, SourceStatistics>>(json);
                    if (data != null)
                    {
                        _statistics = data;
                    }
                }
            }
            catch
            {
                _statistics = new Dictionary<string, SourceStatistics>();
            }
        }

        private void SaveStatistics()
        {
            try
            {
                var json = JsonSerializer.Serialize(_statistics, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_statsFilePath, json);
            }
            catch
            {
            }
        }
    }

    public class SourceStatistics
    {
        public string SourceName { get; set; } = string.Empty;
        public string SourceUrl { get; set; } = string.Empty;
        public int TotalUsageCount { get; set; }
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public int TimeoutCount { get; set; }
        public long TotalResponseTime { get; set; }
        public long AverageResponseTime { get; set; }
        public long BestResponseTime { get; set; }
        public double HealthScore { get; set; } = 100;
        public DateTime LastUsedTime { get; set; } = DateTime.MinValue;
        public DateTime FirstUsedTime { get; set; } = DateTime.Now;

        public void UpdateHealthScore()
        {
            if (TotalUsageCount == 0)
            {
                HealthScore = 100;
                return;
            }

            double score = 100;

            double failureRate = (double)FailureCount / TotalUsageCount;
            score -= failureRate * 40;

            double timeoutRate = (TimeoutCount > 0 ? (double)TimeoutCount / TotalUsageCount : 0);
            score -= timeoutRate * 30;

            if (AverageResponseTime > 500)
                score -= 20;
            else if (AverageResponseTime > 300)
                score -= 10;
            else if (AverageResponseTime > 100)
                score -= 5;

            if (score < 0) score = 0;
            if (score > 100) score = 100;
            
            HealthScore = score;
        }
    }
}
