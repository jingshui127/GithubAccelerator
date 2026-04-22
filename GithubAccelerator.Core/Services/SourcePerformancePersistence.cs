using System.Text.Json;

namespace GithubAccelerator.Services;

/// <summary>
/// 数据源性能历史持久化服务
/// </summary>
public class SourcePerformancePersistence
{
    private static readonly string DataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GithubAccelerator");
    
    private static readonly string HistoryFilePath = Path.Combine(DataDirectory, "source_performance.json");
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public SourcePerformancePersistence()
    {
        if (!Directory.Exists(DataDirectory))
        {
            Directory.CreateDirectory(DataDirectory);
        }
    }

    /// <summary>
    /// 保存性能历史记录到文件
    /// </summary>
    public async Task SaveAsync(Dictionary<string, SourcePerformanceHistory> historyStore)
    {
        try
        {
            var data = new PersistenceData
            {
                Version = 1,
                SaveTime = DateTime.Now,
                Histories = historyStore.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new SourceHistoryData
                    {
                        Url = kvp.Value.Url,
                        TestRecords = kvp.Value.TestRecords.TakeLast(200).ToList(),
                        MetricsHistory = kvp.Value.MetricsHistory.TakeLast(50).ToList(),
                        LastUpdateTime = kvp.Value.LastUpdateTime
                    })
            };

            var json = JsonSerializer.Serialize(data, JsonOptions);
            await File.WriteAllTextAsync(HistoryFilePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"保存性能历史失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 从文件加载性能历史记录
    /// </summary>
    public async Task<Dictionary<string, SourcePerformanceHistory>> LoadAsync()
    {
        try
        {
            if (!File.Exists(HistoryFilePath))
            {
                return new Dictionary<string, SourcePerformanceHistory>();
            }

            var json = await File.ReadAllTextAsync(HistoryFilePath);
            var data = JsonSerializer.Deserialize<PersistenceData>(json, JsonOptions);

            if (data == null || data.Histories == null)
            {
                return new Dictionary<string, SourcePerformanceHistory>();
            }

            return data.Histories.ToDictionary(
                kvp => kvp.Key,
                kvp => new SourcePerformanceHistory
                {
                    Url = kvp.Value.Url,
                    TestRecords = kvp.Value.TestRecords ?? new List<SourceTestRecord>(),
                    MetricsHistory = kvp.Value.MetricsHistory ?? new List<SourcePerformanceMetrics>(),
                    LastUpdateTime = kvp.Value.LastUpdateTime
                });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"加载性能历史失败: {ex.Message}");
            return new Dictionary<string, SourcePerformanceHistory>();
        }
    }

    /// <summary>
    /// 清理过期数据（保留最近 N 天）
    /// </summary>
    public async Task CleanupAsync(int retainDays = 30)
    {
        try
        {
            var history = await LoadAsync();
            var cutoffTime = DateTime.Now.AddDays(-retainDays);

            foreach (var kvp in history)
            {
                kvp.Value.TestRecords.RemoveAll(r => r.TestTime < cutoffTime);
                kvp.Value.MetricsHistory.RemoveAll(m => m.LastTestTime < cutoffTime);
            }

            await SaveAsync(history);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"清理性能历史失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 获取数据文件路径
    /// </summary>
    public string GetDataFilePath() => HistoryFilePath;

    /// <summary>
    /// 获取数据文件大小
    /// </summary>
    public long GetDataFileSize()
    {
        if (!File.Exists(HistoryFilePath)) return 0;
        return new FileInfo(HistoryFilePath).Length;
    }
}

/// <summary>
/// 持久化数据结构
/// </summary>
public class PersistenceData
{
    public int Version { get; set; }
    public DateTime SaveTime { get; set; }
    public Dictionary<string, SourceHistoryData> Histories { get; set; } = new();
}

/// <summary>
/// 数据源历史数据
/// </summary>
public class SourceHistoryData
{
    public string Url { get; set; } = string.Empty;
    public List<SourceTestRecord> TestRecords { get; set; } = new();
    public List<SourcePerformanceMetrics> MetricsHistory { get; set; } = new();
    public DateTime LastUpdateTime { get; set; }
}
