using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace GithubAccelerator.Services;

/// <summary>
/// 数据源性能监控服务
/// </summary>
public interface ISourcePerformanceMonitor
{
    /// <summary>
    /// 启动定时测试
    /// </summary>
    void StartMonitoring();
    
    /// <summary>
    /// 停止定时测试
    /// </summary>
    void StopMonitoring();
    
    /// <summary>
    /// 立即测试所有数据源
    /// </summary>
    Task<SourcePerformanceMetrics[]> TestAllSourcesAsync();
    
    /// <summary>
    /// 获取当前性能指标
    /// </summary>
    SourcePerformanceMetrics[] GetCurrentMetrics();
    
    /// <summary>
    /// 获取性能历史记录
    /// </summary>
    SourcePerformanceHistory GetHistory(string sourceUrl);
    
    /// <summary>
    /// 获取推荐的数据源列表
    /// </summary>
    SourcePerformanceMetrics[] GetRecommendedSources();
    
    /// <summary>
    /// 获取最佳数据源
    /// </summary>
    SourcePerformanceMetrics? GetBestSource();
    
    /// <summary>
    /// 获取所有数据源指标
    /// </summary>
    IEnumerable<SourcePerformanceMetrics> GetAllMetrics();
}

/// <summary>
/// 数据源性能监控服务实现
/// </summary>
public class SourcePerformanceMonitor : ISourcePerformanceMonitor, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SourcePerformanceMonitor> _logger;
    private readonly ConcurrentDictionary<string, SourcePerformanceHistory> _historyStore;
    private readonly ConcurrentDictionary<string, SourcePerformanceMetrics> _currentMetrics;
    private Timer? _monitoringTimer;
    private bool _disposed;
    
    // 配置参数
    private readonly int _testIntervalSeconds = 60; // 默认 60 秒测试一次
    private readonly int _maxHistoryRecords = 1000; // 最多保留 1000 条历史记录
    private readonly int _evaluationWindow = 100; // 评估窗口：最近 100 次测试
    
    public SourcePerformanceMonitor(
        HttpClient httpClient,
        ILogger<SourcePerformanceMonitor> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _historyStore = new ConcurrentDictionary<string, SourcePerformanceHistory>();
        _currentMetrics = new ConcurrentDictionary<string, SourcePerformanceMetrics>();
        
        // 初始化数据源
        InitializeSources();
    }
    
    private void InitializeSources()
    {
        foreach (var source in GithubHostsService.HostsSources)
        {
            _historyStore[source.Url] = new SourcePerformanceHistory
            {
                Url = source.Url
            };
            
            _currentMetrics[source.Url] = new SourcePerformanceMetrics
            {
                Url = source.Url,
                Name = source.Name
            };
        }
    }
    
    /// <summary>
    /// 启动定时监控
    /// </summary>
    public void StartMonitoring()
    {
        if (_monitoringTimer != null)
        {
            return;
        }
        
        _logger.LogInformation("启动数据源性能监控，测试间隔：{Interval}秒", _testIntervalSeconds);
        
        _monitoringTimer = new Timer(
            async _ => await PerformPeriodicTestAsync(),
            null,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(_testIntervalSeconds));
    }
    
    /// <summary>
    /// 停止监控
    /// </summary>
    public void StopMonitoring()
    {
        _monitoringTimer?.Dispose();
        _monitoringTimer = null;
        _logger.LogInformation("停止数据源性能监控");
    }
    
    /// <summary>
    /// 执行周期性测试
    /// </summary>
    private async Task PerformPeriodicTestAsync()
    {
        try
        {
            await TestAllSourcesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "周期性测试失败");
        }
    }
    
    /// <summary>
    /// 测试所有数据源
    /// </summary>
    public async Task<SourcePerformanceMetrics[]> TestAllSourcesAsync()
    {
        var tasks = GithubHostsService.HostsSources.Select(async source =>
        {
            try
            {
                var metrics = await TestSingleSourceAsync(source.Url, source.Name);
                _currentMetrics[source.Url] = metrics;
                return metrics;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "测试数据源 {Url} 失败", source.Url);
                return new SourcePerformanceMetrics
                {
                    Url = source.Url,
                    Name = source.Name,
                    SuccessRate = 0,
                    LastTestTime = DateTime.Now
                };
            }
        });
        
        var results = await Task.WhenAll(tasks);
        return results.OrderByDescending(m => m.OverallScore).ToArray();
    }
    
    /// <summary>
    /// 测试单个数据源
    /// </summary>
    private async Task<SourcePerformanceMetrics> TestSingleSourceAsync(string url, string name)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var record = new SourceTestRecord
        {
            TestTime = DateTime.Now,
            Url = url
        };
        
        try
        {
            var response = await _httpClient.GetAsync(url);
            stopwatch.Stop();
            
            record.ResponseTimeMs = stopwatch.ElapsedMilliseconds;
            record.IsSuccess = response.IsSuccessStatusCode;
            record.HttpStatusCode = (int)response.StatusCode;
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                record.DataSize = content.Length;
                
                // 分析数据质量
                var qualityAnalysis = AnalyzeDataQuality(content);
                record.GithubDomainCount = qualityAnalysis.GithubDomainCount;
                record.DataSize = qualityAnalysis.DataSize;
                
                // 计算各项评分
                var metrics = CalculateMetrics(url, name, record, qualityAnalysis);
                
                // 更新历史记录
                UpdateHistory(url, record, metrics);
                
                return metrics;
            }
            else
            {
                _logger.LogWarning("数据源 {Url} 返回状态码：{StatusCode}", url, response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            record.ResponseTimeMs = stopwatch.ElapsedMilliseconds;
            record.ErrorMessage = ex.Message;
            _logger.LogDebug(ex, "测试数据源 {Url} 异常", url);
        }
        
        // 失败情况
        var failedMetrics = new SourcePerformanceMetrics
        {
            Url = url,
            Name = name,
            LastTestTime = DateTime.Now,
            RecentTestCount = 1,
            ConsecutiveFailures = 1,
            SuccessRate = 0
        };
        
        UpdateHistory(url, record, failedMetrics);
        return failedMetrics;
    }
    
    /// <summary>
    /// 数据质量分析
    /// </summary>
    private (int GithubDomainCount, int DataSize) AnalyzeDataQuality(string content)
    {
        var githubDomainCount = 0;
        var dataSize = 0;
        
        try
        {
            // 统计 GitHub 相关域名数量
            var githubDomains = new[]
            {
                "github.com",
                "api.github.com",
                "raw.githubusercontent.com",
                "githubusercontent.com",
                "githubassets.com",
                "github.io"
            };
            
            foreach (var domain in githubDomains)
            {
                if (content.Contains(domain, StringComparison.OrdinalIgnoreCase))
                {
                    githubDomainCount++;
                }
            }
            
            // 统计有效 hosts 条目数
            var lines = content.Split('\n');
            var validLines = 0;
            
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (!string.IsNullOrEmpty(trimmed) && 
                    !trimmed.StartsWith('#') && 
                    trimmed.Contains(' '))
                {
                    validLines++;
                }
            }
            
            dataSize = validLines;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "数据质量分析失败");
        }
        
        return (githubDomainCount, dataSize);
    }
    
    /// <summary>
    /// 计算性能指标
    /// </summary>
    private SourcePerformanceMetrics CalculateMetrics(
        string url, 
        string name, 
        SourceTestRecord record,
        (int GithubDomainCount, int DataSize) quality)
    {
        // 获取历史记录用于计算统计指标
        var history = _historyStore.GetOrAdd(url, _ => new SourcePerformanceHistory { Url = url });
        var recentRecords = history.GetRecentRecords(_evaluationWindow - 1);
        recentRecords.Add(record);
        
        // 计算成功率
        var successCount = recentRecords.Count(r => r.IsSuccess);
        var successRate = (double)successCount / recentRecords.Count;
        
        // 计算平均响应时间
        var avgResponseTime = recentRecords
            .Where(r => r.IsSuccess)
            .Average(r => r.ResponseTimeMs);
        
        // 计算响应时间标准差
        var responseTimes = recentRecords
            .Where(r => r.IsSuccess)
            .Select(r => r.ResponseTimeMs)
            .ToArray();
        
        var stdDev = responseTimes.Length > 1
            ? Math.Sqrt(responseTimes.Average(x => Math.Pow(x - avgResponseTime, 2)))
            : 0;
        
        // 计算数据完整性评分 (基于 GitHub 域名覆盖度)
        var integrityScore = Math.Min(100, quality.GithubDomainCount * 16.67); // 最多 6 个域名
        
        // 计算数据准确性评分 (基于有效 hosts 数量)
        var accuracyScore = quality.DataSize >= 100 ? 100 : quality.DataSize;
        
        // 计算稳定性评分 (基于标准差，标准差越小越稳定)
        var stabilityScore = Math.Max(0, 100 - (stdDev / 10));
        
        // 计算综合评分
        // 权重：成功率 40%, 响应时间 30%, 稳定性 15%, 完整性 10%, 准确性 5%
        var responseTimeScore = Math.Max(0, 100 - (avgResponseTime / 10));
        var overallScore = (successRate * 40) +
                          (responseTimeScore * 0.3) +
                          (stabilityScore * 0.15) +
                          (integrityScore * 0.1) +
                          (accuracyScore * 0.05);
        
        var speedScore = (successRate * 20) +
                        (responseTimeScore * 0.6) +
                        (stabilityScore * 0.15) +
                        (integrityScore * 0.05);
        
        // 计算连续成功/失败次数
        var consecutiveSuccesses = 0;
        var consecutiveFailures = 0;
        
        for (int i = recentRecords.Count - 1; i >= 0; i--)
        {
            if (recentRecords[i].IsSuccess)
            {
                consecutiveSuccesses++;
                if (consecutiveFailures > 0) break;
            }
            else
            {
                consecutiveFailures++;
                if (consecutiveSuccesses > 0) break;
            }
        }
        
        return new SourcePerformanceMetrics
        {
            Url = url,
            Name = name,
            AverageResponseTimeMs = avgResponseTime,
            SuccessRate = successRate,
            DataIntegrityScore = integrityScore,
            DataAccuracyScore = accuracyScore,
            StabilityScore = stabilityScore,
            OverallScore = overallScore,
            SpeedScore = speedScore,
            LastTestTime = DateTime.Now,
            RecentTestCount = recentRecords.Count,
            ConsecutiveSuccesses = consecutiveSuccesses,
            ConsecutiveFailures = consecutiveFailures,
            ResponseTimeStdDev = stdDev
        };
    }
    
    /// <summary>
    /// 更新历史记录
    /// </summary>
    private void UpdateHistory(string url, SourceTestRecord record, SourcePerformanceMetrics metrics)
    {
        var history = _historyStore.GetOrAdd(url, _ => new SourcePerformanceHistory { Url = url });
        history.AddTestRecord(record);
        history.MetricsHistory.Add(metrics);
        
        // 保留最近 100 个性能指标
        if (history.MetricsHistory.Count > 100)
        {
            history.MetricsHistory.RemoveAt(0);
        }
    }
    
    /// <summary>
    /// 获取当前性能指标
    /// </summary>
    public SourcePerformanceMetrics[] GetCurrentMetrics()
    {
        return _currentMetrics.Values
            .OrderByDescending(m => m.OverallScore)
            .ToArray();
    }
    
    /// <summary>
    /// 获取性能历史记录
    /// </summary>
    public SourcePerformanceHistory GetHistory(string sourceUrl)
    {
        return _historyStore.TryGetValue(sourceUrl, out var history) 
            ? history 
            : new SourcePerformanceHistory { Url = sourceUrl };
    }
    
    /// <summary>
    /// 获取推荐的数据源列表
    /// </summary>
    public SourcePerformanceMetrics[] GetRecommendedSources()
    {
        return _currentMetrics.Values
            .Where(m => m.IsRecommended)
            .OrderByDescending(m => m.SpeedScore)
            .ThenBy(m => m.AverageResponseTimeMs)
            .ToArray();
    }
    
    /// <summary>
    /// 获取最佳数据源
    /// </summary>
    public SourcePerformanceMetrics? GetBestSource()
    {
        return _currentMetrics.Values
            .Where(m => m.IsRecommended)
            .OrderByDescending(m => m.SpeedScore)
            .ThenBy(m => m.AverageResponseTimeMs)
            .FirstOrDefault();
    }
    
    /// <summary>
    /// 获取所有数据源指标
    /// </summary>
    public IEnumerable<SourcePerformanceMetrics> GetAllMetrics()
    {
        return _currentMetrics.Values;
    }
    
    /// <summary>
    /// 设置测试间隔
    /// </summary>
    public void SetTestInterval(int seconds)
    {
        _monitoringTimer?.Change(TimeSpan.Zero, TimeSpan.FromSeconds(seconds));
        _logger.LogInformation("更新测试间隔为 {Interval}秒", seconds);
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        
        _monitoringTimer?.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
