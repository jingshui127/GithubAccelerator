using System.Linq;
using System.Reflection;
using GithubAccelerator.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

// ReSharper disable AccessToThisInConstructor - 测试类构造函数中访问 _selector 是正常的

namespace GithubAccelerator.Tests;

/// <summary>
/// SmartSourceSelector 单元测试 - 验证数据源推荐算法、优先级调整、健康状态管理
/// </summary>
public class SmartSourceSelectorTests
{
    private readonly SmartSourceSelector _selector;
    private readonly ILogger<SmartSourceSelector> _logger;

    public SmartSourceSelectorTests()
    {
        _logger = NullLogger<SmartSourceSelector>.Instance;
        _selector = new SmartSourceSelector(_logger);
    }

    // ===== 构造与初始化 =====

    [Fact]
    public void Constructor_InitializesWithDefaultSources()
    {
        var selector = new SmartSourceSelector(_logger);
        var recommended = selector.GetRecommendedSources();
        Assert.NotEmpty(recommended);
    }

    [Fact]
    public void GetRecommendedSources_WithoutMetrics_ReturnsHealthySources()
    {
        var sources = _selector.GetRecommendedSources();
        // 默认数据源都应该是健康的
        Assert.All(sources, s => Assert.True(s.IsHealthy));
    }

    [Fact]
    public void GetBestSource_ReturnsFirstRecommended()
    {
        var best = _selector.GetBestSource();
        var recommended = _selector.GetRecommendedSources();

        Assert.NotNull(best);
        Assert.Equal(recommended[0].Url, best.Url);
    }

    [Fact]
    public void GetTopSources_Zero_ReturnsEmptyArray()
    {
        var top = _selector.GetTopSources(0);
        Assert.Empty(top);
    }

    [Fact]
    public void GetTopSources_CountGreaterThanAvailable_ReturnsAll()
    {
        var all = _selector.GetRecommendedSources();
        var top = _selector.GetTopSources(all.Length + 10);
        Assert.Equal(all.Length, top.Length);
    }

    [Fact]
    public void GetTopSources_PositiveCount_ReturnsAtMostCount()
    {
        var top = _selector.GetTopSources(1);
        Assert.Single(top);
    }

    // ===== UpdatePrioritiesFromMetrics - 评分等级映射 =====
    // 注意：IsRecommended 要求 (OverallScore >= 60 && SuccessRate >= 0.8) || (SpeedScore >= 50 && SuccessRate >= 0.8)
    // 所以测试用例的 successRate 都至少 0.8 才能保证源仍被推荐

    [Fact]
    public void UpdatePrioritiesFromMetrics_ScoreAbove90_SetsPriority1()
    {
        var source = _selector.GetRecommendedSources().First();
        var metric = CreateMetric(source.Url, source.Name, overallScore: 95, successRate: 0.95);

        _selector.UpdatePrioritiesFromMetrics(new[] { metric });

        var updated = GetSourceByUrl(source.Url);
        Assert.Equal(1, updated.Priority);
    }

    [Fact]
    public void UpdatePrioritiesFromMetrics_Score80_SetsPriority2()
    {
        var source = _selector.GetRecommendedSources().First();
        var metric = CreateMetric(source.Url, source.Name, overallScore: 85, successRate: 0.9);

        _selector.UpdatePrioritiesFromMetrics(new[] { metric });

        var updated = GetSourceByUrl(source.Url);
        Assert.Equal(2, updated.Priority);
    }

    [Fact]
    public void UpdatePrioritiesFromMetrics_Score70_SetsPriority3()
    {
        var source = _selector.GetRecommendedSources().First();
        var metric = CreateMetric(source.Url, source.Name, overallScore: 75, successRate: 0.85);

        _selector.UpdatePrioritiesFromMetrics(new[] { metric });

        var updated = GetSourceByUrl(source.Url);
        Assert.Equal(3, updated.Priority);
    }

    [Fact]
    public void UpdatePrioritiesFromMetrics_Score60_SetsPriority4()
    {
        var source = _selector.GetRecommendedSources().First();
        var metric = CreateMetric(source.Url, source.Name, overallScore: 65, successRate: 0.85);

        _selector.UpdatePrioritiesFromMetrics(new[] { metric });

        var updated = GetSourceByUrl(source.Url);
        Assert.Equal(4, updated.Priority);
    }

    [Fact]
    public void UpdatePrioritiesFromMetrics_ScoreBelow60_SetsPriority5()
    {
        var source = _selector.GetRecommendedSources().First();
        // Score=50 但 SpeedScore=60 + SuccessRate=0.85 仍满足 IsRecommended 第二个条件
        var metric = CreateMetric(source.Url, source.Name, overallScore: 50, successRate: 0.85);
        metric.SpeedScore = 60;

        _selector.UpdatePrioritiesFromMetrics(new[] { metric });

        var updated = GetSourceByUrl(source.Url);
        Assert.Equal(5, updated.Priority);
    }

    // ===== UpdatePrioritiesFromMetrics - 连续失败/成功调整 =====

    [Fact]
    public void UpdatePrioritiesFromMetrics_ConsecutiveFailures3_LowersPriority()
    {
        var source = _selector.GetRecommendedSources().First();
        // 初始评分为 95（priority=1），连续失败 3 次后 Math.Max(10, 1+3)=10
        var metric = CreateMetric(source.Url, source.Name, overallScore: 95, successRate: 0.85);
        metric.ConsecutiveFailures = 3;

        _selector.UpdatePrioritiesFromMetrics(new[] { metric });

        var updated = GetSourceByUrl(source.Url);
        // 95 -> 1, 连续失败 3 次触发 Math.Max(10, 1+3)=10
        Assert.Equal(10, updated.Priority);
    }

    [Fact]
    public void UpdatePrioritiesFromMetrics_ConsecutiveFailures_MoreThan5_PriorityAtLeast10()
    {
        var source = _selector.GetRecommendedSources().First();
        var metric = CreateMetric(source.Url, source.Name, overallScore: 50, successRate: 0.85);
        metric.SpeedScore = 60; // 保证 IsRecommended=true
        metric.ConsecutiveFailures = 5;

        _selector.UpdatePrioritiesFromMetrics(new[] { metric });

        var updated = GetSourceByUrl(source.Url);
        // 50 -> 5, +3 -> 8 (Math.Max(10, 8) = 10)
        Assert.True(updated.Priority >= 10, $"连续失败 5 次应保证优先级 >=10，实际: {updated.Priority}");
    }

    [Fact]
    public void UpdatePrioritiesFromMetrics_ConsecutiveSuccesses10_ImprovesPriority()
    {
        var source = _selector.GetRecommendedSources().First();
        // 初始评分为 75（priority=3），连续成功 10 次后应提升至 2
        var metric = CreateMetric(source.Url, source.Name, overallScore: 75, successRate: 0.95);
        metric.ConsecutiveSuccesses = 10;

        _selector.UpdatePrioritiesFromMetrics(new[] { metric });

        var updated = GetSourceByUrl(source.Url);
        // 75 -> 3, 连续成功 -1 -> 2
        Assert.Equal(2, updated.Priority);
    }

    [Fact]
    public void UpdatePrioritiesFromMetrics_ConsecutiveSuccessesAtPriority1_StaysAt1()
    {
        var source = _selector.GetRecommendedSources().First();
        var metric = CreateMetric(source.Url, source.Name, overallScore: 95, successRate: 0.95);
        metric.ConsecutiveSuccesses = 100;

        _selector.UpdatePrioritiesFromMetrics(new[] { metric });

        var updated = GetSourceByUrl(source.Url);
        Assert.Equal(1, updated.Priority); // 已经是最高优先级，无法再提升
    }

    // ===== UpdatePrioritiesFromMetrics - 健康状态 =====
    // 注意：IsHealthy 的判断是 source.IsHealthy = metric.SuccessRate >= 0.5（与 IsRecommended 不同）
    // 但通过 GetRecommendedSources 获取的源都已通过 IsRecommended 过滤
    // 此处通过反射访问 _sourcesCache 来验证 IsHealthy 字段

    [Fact]
    public void UpdatePrioritiesFromMetrics_SuccessRateAbove50_SetsHealthy()
    {
        var source = _selector.GetRecommendedSources().First();
        var metric = CreateMetric(source.Url, source.Name, overallScore: 80, successRate: 0.6);
        metric.SpeedScore = 60; // 保证 IsRecommended=true 以便后续断言

        _selector.UpdatePrioritiesFromMetrics(new[] { metric });

        var updated = GetSourceByUrl(source.Url);
        Assert.True(updated.IsHealthy);
    }

    [Fact]
    public void UpdatePrioritiesFromMetrics_SuccessRateBelow50_SetsUnhealthy()
    {
        var source = _selector.GetRecommendedSources().First();
        // SpeedScore >= 50 + SuccessRate >= 0.8 才满足 IsRecommended
        // 此处 SuccessRate=0.4 不满足，但通过反射仍可读取 _sourcesCache 中的源
        var metric = CreateMetric(source.Url, source.Name, overallScore: 80, successRate: 0.4);
        metric.SpeedScore = 60;
        metric.ConsecutiveSuccesses = 15; // 提升优先级让测试更清晰

        _selector.UpdatePrioritiesFromMetrics(new[] { metric });

        var updated = GetSourceByUrl(source.Url);
        Assert.False(updated.IsHealthy);
    }

    [Fact]
    public void UpdatePrioritiesFromMetrics_SuccessRateExactly50_SetsHealthy()
    {
        var source = _selector.GetRecommendedSources().First();
        var metric = CreateMetric(source.Url, source.Name, overallScore: 80, successRate: 0.5);
        metric.SpeedScore = 60;

        _selector.UpdatePrioritiesFromMetrics(new[] { metric });

        var updated = GetSourceByUrl(source.Url);
        Assert.True(updated.IsHealthy); // >= 0.5
    }

    // ===== UpdatePrioritiesFromMetrics - 多个数据源 =====

    [Fact]
    public void UpdatePrioritiesFromMetrics_MultipleSources_AllUpdated()
    {
        var sources = _selector.GetRecommendedSources().Take(2).ToList();
        var metrics = new[]
        {
            CreateMetric(sources[0].Url, sources[0].Name, overallScore: 95, successRate: 0.95),
            CreateMetric(sources[1].Url, sources[1].Name, overallScore: 50, successRate: 0.85)
        };
        metrics[1].SpeedScore = 60; // 保证第二个源 IsRecommended=true

        _selector.UpdatePrioritiesFromMetrics(metrics);

        var updated = _selector.GetRecommendedSources();
        Assert.Contains(updated, s => s.Url == sources[0].Url && s.Priority == 1);
        Assert.Contains(updated, s => s.Url == sources[1].Url && s.Priority == 5);
    }

    [Fact]
    public void UpdatePrioritiesFromMetrics_EmptyArray_DoesNotThrow()
    {
        var exception = Record.Exception(() => _selector.UpdatePrioritiesFromMetrics(Array.Empty<SourcePerformanceMetrics>()));
        Assert.Null(exception);
    }

    [Fact]
    public void UpdatePrioritiesFromMetrics_UnknownUrl_DoesNotThrow()
    {
        var metric = CreateMetric("https://nonexistent-url.test", "Unknown", 90, 0.95);
        var exception = Record.Exception(() => _selector.UpdatePrioritiesFromMetrics(new[] { metric }));
        Assert.Null(exception);
    }

    // ===== GetPerformanceReport =====

    [Fact]
    public void GetPerformanceReport_ContainsHeaderAndSeparator()
    {
        var report = _selector.GetPerformanceReport();
        Assert.Contains("数据源性能报告", report);
        Assert.Contains("===", report);
    }

    [Fact]
    public void GetPerformanceReport_WithMetrics_ContainsDetailedInfo()
    {
        var source = _selector.GetRecommendedSources().First();
        var metric = CreateMetric(source.Url, source.Name, overallScore: 85, successRate: 0.9);
        metric.AverageResponseTimeMs = 150;
        metric.StabilityScore = 88;
        metric.DataIntegrityScore = 92;
        metric.ConsecutiveSuccesses = 5;

        _selector.UpdatePrioritiesFromMetrics(new[] { metric });

        var report = _selector.GetPerformanceReport();
        Assert.Contains(source.Name, report);
        Assert.Contains("综合评分", report);
        Assert.Contains("成功率", report);
        Assert.Contains("平均响应", report);
        Assert.Contains("稳定性", report);
        Assert.Contains("完整性", report);
        Assert.Contains("连续成功", report);
    }

    [Fact]
    public void GetPerformanceReport_WithFailures_ContainsWarning()
    {
        var source = _selector.GetRecommendedSources().First();
        // 让源保持 IsRecommended=true (SpeedScore >= 50 + SuccessRate >= 0.8)
        var metric = CreateMetric(source.Url, source.Name, overallScore: 60, successRate: 0.85);
        metric.SpeedScore = 60;
        metric.ConsecutiveFailures = 4;

        _selector.UpdatePrioritiesFromMetrics(new[] { metric });

        var report = _selector.GetPerformanceReport();
        Assert.Contains("连续失败", report);
        Assert.Contains("⚠", report);
    }

    // ===== RefreshSources =====

    [Fact]
    public void RefreshSources_DoesNotThrow()
    {
        var exception = Record.Exception(() => _selector.RefreshSources());
        Assert.Null(exception);
    }

    [Fact]
    public void RefreshSources_PreservesPerformanceData()
    {
        var source = _selector.GetRecommendedSources().First();
        var metric = CreateMetric(source.Url, source.Name, overallScore: 90, successRate: 0.95);

        _selector.UpdatePrioritiesFromMetrics(new[] { metric });
        _selector.RefreshSources();

        var report = _selector.GetPerformanceReport();
        Assert.Contains("综合评分", report); // 性能数据仍然存在
    }

    // ===== 排序逻辑 =====

    [Fact]
    public void GetRecommendedSources_OrdersBySpeedScoreDescending()
    {
        var sources = _selector.GetRecommendedSources().Take(2).ToList();
        var lowScoreMetric = CreateMetric(sources[0].Url, sources[0].Name, overallScore: 80, successRate: 0.9);
        lowScoreMetric.SpeedScore = 50;
        var highScoreMetric = CreateMetric(sources[1].Url, sources[1].Name, overallScore: 80, successRate: 0.9);
        highScoreMetric.SpeedScore = 90;

        _selector.UpdatePrioritiesFromMetrics(new[] { lowScoreMetric, highScoreMetric });

        var recommended = _selector.GetRecommendedSources();
        // 高分应该排在前面
        var highIndex = Array.FindIndex(recommended, s => s.Url == sources[1].Url);
        var lowIndex = Array.FindIndex(recommended, s => s.Url == sources[0].Url);
        Assert.True(highIndex < lowIndex, "SpeedScore 高的数据源应排在前面");
    }

    // ===== IsSourceRecommended 边界 =====

    [Fact]
    public void GetRecommendedSources_ExcludesUnhealthySourcesWithoutMetrics()
    {
        var source = _selector.GetRecommendedSources().First();
        var metric = CreateMetric(source.Url, source.Name, overallScore: 30, successRate: 0.2);
        // IsRecommended => (OverallScore >= 60 && SuccessRate >= 0.8) || (SpeedScore >= 50 && SuccessRate >= 0.8)
        // 30 和 0.2 都不满足，IsRecommended = false

        _selector.UpdatePrioritiesFromMetrics(new[] { metric });

        var recommended = _selector.GetRecommendedSources();
        Assert.DoesNotContain(recommended, s => s.Url == source.Url);
    }

    [Fact]
    public void GetRecommendedSources_IncludesHighSpeedLowOverall()
    {
        var source = _selector.GetRecommendedSources().First();
        var metric = CreateMetric(source.Url, source.Name, overallScore: 40, successRate: 0.85);
        metric.SpeedScore = 60; // SpeedScore >= 50 && SuccessRate >= 0.8 => IsRecommended = true

        _selector.UpdatePrioritiesFromMetrics(new[] { metric });

        var recommended = _selector.GetRecommendedSources();
        Assert.Contains(recommended, s => s.Url == source.Url);
    }

    // ===== 辅助方法 =====

    private static SourcePerformanceMetrics CreateMetric(string url, string name, double overallScore, double successRate)
    {
        return new SourcePerformanceMetrics
        {
            Url = url,
            Name = name,
            OverallScore = overallScore,
            SuccessRate = successRate,
            SpeedScore = overallScore, // 默认 SpeedScore 与 OverallScore 相同
            LastTestTime = DateTime.Now,
            RecentTestCount = 10
        };
    }

    /// <summary>
    /// 通过反射访问私有 _sourcesCache，绕过 IsRecommended 过滤
    /// </summary>
    private HostsSource GetSourceByUrl(string url)
    {
        var field = typeof(SmartSourceSelector).GetField("_sourcesCache",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        var cache = field!.GetValue(_selector) as System.Collections.Concurrent.ConcurrentDictionary<string, HostsSource>;
        Assert.NotNull(cache);
        Assert.True(cache!.TryGetValue(url, out var source), $"源 {url} 不存在于 _sourcesCache 中");
        return source!;
    }
}
