using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace GithubAccelerator.Services;

/// <summary>
/// 智能数据源选择器 - 基于性能评估动态调整优先级
/// </summary>
public interface ISmartSourceSelector
{
    /// <summary>
    /// 获取推荐的数据源（按优先级排序）
    /// </summary>
    HostsSource[] GetRecommendedSources();
    
    /// <summary>
    /// 获取最佳数据源
    /// </summary>
    HostsSource? GetBestSource();
    
    /// <summary>
    /// 获取前 N 个推荐数据源
    /// </summary>
    HostsSource[] GetTopSources(int count);
    
    /// <summary>
    /// 根据性能指标更新数据源优先级
    /// </summary>
    void UpdatePrioritiesFromMetrics(SourcePerformanceMetrics[] metrics);
    
    /// <summary>
    /// 获取数据源性能报告
    /// </summary>
    string GetPerformanceReport();
}

/// <summary>
/// 智能数据源选择器实现
/// </summary>
public class SmartSourceSelector : ISmartSourceSelector
{
    private readonly ILogger<SmartSourceSelector> _logger;
    private readonly ConcurrentDictionary<string, HostsSource> _sourcesCache;
    private readonly ConcurrentDictionary<string, SourcePerformanceMetrics> _performanceCache;
    private bool _prioritiesUpdated;
    
    public SmartSourceSelector(ILogger<SmartSourceSelector> logger)
    {
        _logger = logger;
        _sourcesCache = new ConcurrentDictionary<string, HostsSource>();
        _performanceCache = new ConcurrentDictionary<string, SourcePerformanceMetrics>();
        _prioritiesUpdated = false;
        
        // 初始化数据源缓存
        InitializeSources();
    }
    
    private void InitializeSources()
    {
        var sources = GithubHostsService.HostsSources;
        foreach (var source in sources)
        {
            _sourcesCache[source.Url] = CloneSource(source);
        }
        _logger.LogInformation("初始化 {Count} 个数据源", sources.Length);
    }
    
    private static HostsSource CloneSource(HostsSource source)
    {
        return new HostsSource
        {
            Url = source.Url,
            Name = source.Name,
            Description = source.Description,
            Priority = source.Priority,
            IsHealthy = source.IsHealthy,
            LastResponseTimeMs = source.LastResponseTimeMs,
            LastCheckTime = source.LastCheckTime
        };
    }
    
    /// <summary>
    /// 根据性能指标更新数据源优先级
    /// </summary>
    public void UpdatePrioritiesFromMetrics(SourcePerformanceMetrics[] metrics)
    {
        foreach (var metric in metrics)
        {
            _performanceCache[metric.Url] = metric;
            
            if (_sourcesCache.TryGetValue(metric.Url, out var source))
            {
                // 根据综合评分动态调整优先级
                // 评分越高，优先级数字越小（优先级越高）
                int newPriority;
                
                if (metric.OverallScore >= 90) newPriority = 1;  // S 级
                else if (metric.OverallScore >= 80) newPriority = 2;  // A 级
                else if (metric.OverallScore >= 70) newPriority = 3;  // B 级
                else if (metric.OverallScore >= 60) newPriority = 4;  // C 级
                else newPriority = 5;  // D 级 - 不推荐但保留
                
                // 连续失败多次的数据源，降低优先级
                if (metric.ConsecutiveFailures >= 3)
                {
                    newPriority = Math.Max(10, newPriority + 3);
                    _logger.LogWarning("数据源 {Name} 连续失败 {Count} 次，降低优先级", metric.Name, metric.ConsecutiveFailures);
                }
                
                // 连续成功多次的数据源，提升优先级
                if (metric.ConsecutiveSuccesses >= 10 && newPriority > 1)
                {
                    newPriority = Math.Max(1, newPriority - 1);
                }
                
                source.Priority = newPriority;
                source.IsHealthy = metric.SuccessRate >= 0.5;
            }
        }
        
        _prioritiesUpdated = true;
        _logger.LogInformation("已根据性能指标更新数据源优先级");
    }
    
    /// <summary>
    /// 获取推荐的数据源（按优先级排序）
    /// </summary>
    public HostsSource[] GetRecommendedSources()
    {
        return _sourcesCache.Values
            .Where(s => IsSourceRecommended(s))
            .OrderBy(s => s.Priority)
            .ThenByDescending(s => GetPerformanceScore(s.Url))
            .ToArray();
    }
    
    /// <summary>
    /// 获取最佳数据源
    /// </summary>
    public HostsSource? GetBestSource()
    {
        return GetRecommendedSources().FirstOrDefault();
    }
    
    /// <summary>
    /// 获取前 N 个推荐数据源
    /// </summary>
    public HostsSource[] GetTopSources(int count)
    {
        return GetRecommendedSources().Take(count).ToArray();
    }
    
    /// <summary>
    /// 判断数据源是否推荐
    /// </summary>
    private bool IsSourceRecommended(HostsSource source)
    {
        // 如果有性能数据，基于性能判断
        if (_performanceCache.TryGetValue(source.Url, out var metrics))
        {
            return metrics.IsRecommended;
        }
        
        // 否则基于健康状态判断
        return source.IsHealthy && source.Priority <= 3;
    }
    
    /// <summary>
    /// 获取性能评分
    /// </summary>
    private double GetPerformanceScore(string url)
    {
        if (_performanceCache.TryGetValue(url, out var metrics))
        {
            return metrics.SpeedScore;
        }
        
        var source = _sourcesCache.GetValueOrDefault(url);
        return source != null ? 100 - (source.Priority * 10) : 0;
    }
    
    /// <summary>
    /// 获取性能报告
    /// </summary>
    public string GetPerformanceReport()
    {
        var report = new System.Text.StringBuilder();
        report.AppendLine("=== 数据源性能报告 ===");
        report.AppendLine();
        
        var sources = GetRecommendedSources();
        foreach (var source in sources)
        {
            var metrics = _performanceCache.GetValueOrDefault(source.Url);
            
            report.AppendLine($"【{source.Name}】");
            report.AppendLine($"  URL: {source.Url}");
            report.AppendLine($"  优先级：{source.Priority}");
            report.AppendLine($"  健康状态：{(source.IsHealthy ? "✓ 健康" : "✗ 异常")}");
            
            if (metrics != null)
            {
                report.AppendLine($"  综合评分：{metrics.OverallScore:F1} ({metrics.RecommendationLevel}级)");
                report.AppendLine($"  成功率：{metrics.SuccessRate:P1}");
                report.AppendLine($"  平均响应：{metrics.AverageResponseTimeMs:F0}ms");
                report.AppendLine($"  稳定性：{metrics.StabilityScore:F1}");
                report.AppendLine($"  完整性：{metrics.DataIntegrityScore:F1}");
                report.AppendLine($"  连续成功：{metrics.ConsecutiveSuccesses}");
                
                if (metrics.ConsecutiveFailures > 0)
                {
                    report.AppendLine($"  ⚠ 连续失败：{metrics.ConsecutiveFailures}");
                }
            }
            
            report.AppendLine();
        }
        
        report.AppendLine("===================");
        return report.ToString();
    }
    
    /// <summary>
    /// 刷新数据源列表
    /// </summary>
    public void RefreshSources()
    {
        _sourcesCache.Clear();
        InitializeSources();
        
        // 保留性能数据
        _logger.LogInformation("已刷新数据源列表");
    }
}
