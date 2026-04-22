namespace GithubAccelerator.Services;

/// <summary>
/// 数据源性能评估指标
/// </summary>
public class SourcePerformanceMetrics
{
    /// <summary>
    /// 数据源 URL
    /// </summary>
    public string Url { get; set; } = string.Empty;
    
    /// <summary>
    /// 数据源名称
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// 平均响应时间 (毫秒)
    /// </summary>
    public double AverageResponseTimeMs { get; set; }
    
    /// <summary>
    /// 成功率 (0-1)
    /// </summary>
    public double SuccessRate { get; set; }
    
    /// <summary>
    /// 数据完整性评分 (0-100)
    /// </summary>
    public double DataIntegrityScore { get; set; }
    
    /// <summary>
    /// 数据准确性评分 (0-100)
    /// </summary>
    public double DataAccuracyScore { get; set; }
    
    /// <summary>
    /// 稳定性评分 (0-100) - 基于响应时间标准差
    /// </summary>
    public double StabilityScore { get; set; }
    
    /// <summary>
    /// 综合评分 (0-100)
    /// </summary>
    public double OverallScore { get; set; }
    
    public double SpeedScore { get; set; }
    
    /// <summary>
    /// 最后测试时间
    /// </summary>
    public DateTime LastTestTime { get; set; }
    
    /// <summary>
    /// 最近测试次数
    /// </summary>
    public int RecentTestCount { get; set; }
    
    /// <summary>
    /// 连续成功次数
    /// </summary>
    public int ConsecutiveSuccesses { get; set; }
    
    /// <summary>
    /// 连续失败次数
    /// </summary>
    public int ConsecutiveFailures { get; set; }
    
    /// <summary>
    /// 响应时间标准差
    /// </summary>
    public double ResponseTimeStdDev { get; set; }
    
    /// <summary>
    /// 是否推荐使用
    /// </summary>
    public bool IsRecommended => OverallScore >= 60 && SuccessRate >= 0.8;
    
    /// <summary>
    /// 推荐等级 (S/A/B/C/D)
    /// </summary>
    public string RecommendationLevel
    {
        get
        {
            if (OverallScore >= 90) return "S";
            if (OverallScore >= 80) return "A";
            if (OverallScore >= 70) return "B";
            if (OverallScore >= 60) return "C";
            return "D";
        }
    }
}

/// <summary>
/// 单次测试记录
/// </summary>
public class SourceTestRecord
{
    /// <summary>
    /// 测试时间
    /// </summary>
    public DateTime TestTime { get; set; }
    
    /// <summary>
    /// 数据源 URL
    /// </summary>
    public string Url { get; set; } = string.Empty;
    
    /// <summary>
    /// 响应时间 (毫秒)
    /// </summary>
    public long ResponseTimeMs { get; set; }
    
    /// <summary>
    /// 是否成功
    /// </summary>
    public bool IsSuccess { get; set; }
    
    /// <summary>
    /// HTTP 状态码
    /// </summary>
    public int? HttpStatusCode { get; set; }
    
    /// <summary>
    /// 数据大小 (字节)
    /// </summary>
    public int DataSize { get; set; }
    
    /// <summary>
    /// GitHub 域名数量
    /// </summary>
    public int GithubDomainCount { get; set; }
    
    /// <summary>
    /// 错误信息
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// 数据源性能历史记录
/// </summary>
public class SourcePerformanceHistory
{
    /// <summary>
    /// 数据源 URL
    /// </summary>
    public string Url { get; set; } = string.Empty;
    
    /// <summary>
    /// 测试记录列表
    /// </summary>
    public List<SourceTestRecord> TestRecords { get; set; } = new();
    
    /// <summary>
    /// 性能指标历史记录
    /// </summary>
    public List<SourcePerformanceMetrics> MetricsHistory { get; set; } = new();
    
    /// <summary>
    /// 最后更新时间
    /// </summary>
    public DateTime LastUpdateTime { get; set; }
    
    /// <summary>
    /// 添加测试记录
    /// </summary>
    public void AddTestRecord(SourceTestRecord record)
    {
        TestRecords.Add(record);
        
        // 保留最近 1000 条记录
        if (TestRecords.Count > 1000)
        {
            TestRecords.RemoveAt(0);
        }
        
        LastUpdateTime = DateTime.Now;
    }
    
    /// <summary>
    /// 获取最近 N 条记录
    /// </summary>
    public List<SourceTestRecord> GetRecentRecords(int count = 100)
    {
        return TestRecords.OrderByDescending(r => r.TestTime).Take(count).ToList();
    }
}
