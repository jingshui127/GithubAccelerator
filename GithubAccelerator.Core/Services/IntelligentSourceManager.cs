using System.Collections.Concurrent;
using System.Text.Json;

namespace GithubAccelerator.Services;

public interface IIntelligentSourceManager
{
    void Initialize();
    void StartSmartMonitoring();
    void StopSmartMonitoring();
    Task<HostsSource?> GetBestAvailableSourceAsync();
    IEnumerable<HostsSource> GetHealthySources();
    SourceHealthReport GetHealthReport();
    Task RefreshSourcesAsync();
}

public class SourceHealthReport
{
    public int TotalSources { get; set; }
    public int HealthySources { get; set; }
    public int UnhealthySources { get; set; }
    public List<SourceHealthInfo> Sources { get; set; } = new();
    public DateTime LastRefreshTime { get; set; }
}

public class SourceHealthInfo
{
    public string Url { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsHealthy { get; set; }
    public long ResponseTimeMs { get; set; }
    public double SuccessRate { get; set; }
    public int TestCount { get; set; }
    public DateTime? LastCheckTime { get; set; }
    public string Status => IsHealthy ? "健康" : "不可用";
}

public class IntelligentSourceManager : IIntelligentSourceManager, IDisposable
{
    private readonly ConcurrentDictionary<string, SourceHealthInfo> _sourceHealthMap = new();
    private Timer? _monitoringTimer;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private bool _isInitialized;
    private bool _isMonitoring;
    private readonly TimeSpan _checkInterval = TimeSpan.FromHours(1);
    private readonly TimeSpan _initialDelay = TimeSpan.FromHours(1);

    public void Initialize()
    {
        if (_isInitialized) return;

        foreach (var source in GithubHostsService.HostsSources)
        {
            _sourceHealthMap[source.Url] = new SourceHealthInfo
            {
                Url = source.Url,
                Name = source.Name,
                IsHealthy = true,
                TestCount = 0
            };
        }

        _isInitialized = true;
    }

    public void StartSmartMonitoring()
    {
        if (_isMonitoring) return;

        _isMonitoring = true;
        _monitoringTimer = new Timer(
            async _ => await PerformHealthCheckAsync(),
            null,
            _initialDelay,
            _checkInterval);

        _ = PerformHealthCheckAsync();
    }

    public void StopSmartMonitoring()
    {
        _isMonitoring = false;
        _monitoringTimer?.Dispose();
        _monitoringTimer = null;
    }

    private async Task PerformHealthCheckAsync()
    {
        if (!await _refreshLock.WaitAsync(0))
            return;

        try
        {
            var tasks = GithubHostsService.HostsSources.Select(async source =>
            {
                var health = await CheckSourceHealthAsync(source);
                _sourceHealthMap[source.Url] = health;
                return health;
            });

            await Task.WhenAll(tasks);
            UpdateSourceHealthStatus();
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<SourceHealthInfo> CheckSourceHealthAsync(HostsSource source)
    {
        var health = new SourceHealthInfo
        {
            Url = source.Url,
            Name = source.Name,
            LastCheckTime = DateTime.Now
        };

        var existingHealth = _sourceHealthMap.GetValueOrDefault(source.Url);

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var response = await GithubHostsService.HttpClient.GetAsync(source.Url, cts.Token);
            sw.Stop();

            health.ResponseTimeMs = sw.ElapsedMilliseconds;
            health.IsHealthy = response.IsSuccessStatusCode;
            health.TestCount = (existingHealth?.TestCount ?? 0) + 1;

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cts.Token);
                health.SuccessRate = content.Contains("github.com") ? 1.0 : 0.5;
            }
            else
            {
                health.SuccessRate = 0;
            }
        }
        catch
        {
            health.IsHealthy = false;
            health.ResponseTimeMs = -1;
            health.SuccessRate = 0;
            health.TestCount = (existingHealth?.TestCount ?? 0) + 1;
        }

        return health;
    }

    private void UpdateSourceHealthStatus()
    {
        foreach (var source in GithubHostsService.HostsSources)
        {
            if (_sourceHealthMap.TryGetValue(source.Url, out var health))
            {
                source.IsHealthy = health.IsHealthy;
                source.LastResponseTimeMs = health.ResponseTimeMs;
                source.LastCheckTime = health.LastCheckTime;
            }
        }
    }

    public async Task<HostsSource?> GetBestAvailableSourceAsync()
    {
        if (!_isInitialized)
            Initialize();

        if (_sourceHealthMap.IsEmpty)
            await RefreshSourcesAsync();

        var healthySources = _sourceHealthMap.Values
            .Where(h => h.IsHealthy)
            .OrderBy(h => h.ResponseTimeMs)
            .ToList();

        if (healthySources.Count == 0)
        {
            return GithubHostsService.HostsSources.FirstOrDefault();
        }

        var best = healthySources.First();
        return GithubHostsService.HostsSources.FirstOrDefault(s => s.Url == best.Url);
    }

    public IEnumerable<HostsSource> GetHealthySources()
    {
        return GithubHostsService.HostsSources.Where(s => s.IsHealthy);
    }

    public SourceHealthReport GetHealthReport()
    {
        var report = new SourceHealthReport
        {
            TotalSources = GithubHostsService.HostsSources.Length,
            HealthySources = _sourceHealthMap.Values.Count(h => h.IsHealthy),
            UnhealthySources = _sourceHealthMap.Values.Count(h => !h.IsHealthy),
            LastRefreshTime = DateTime.Now
        };

        foreach (var source in GithubHostsService.HostsSources)
        {
            var health = _sourceHealthMap.GetValueOrDefault(source.Url);
            report.Sources.Add(new SourceHealthInfo
            {
                Url = source.Url,
                Name = source.Name,
                IsHealthy = health?.IsHealthy ?? true,
                ResponseTimeMs = health?.ResponseTimeMs ?? 0,
                SuccessRate = health?.SuccessRate ?? 1.0,
                TestCount = health?.TestCount ?? 0,
                LastCheckTime = health?.LastCheckTime
            });
        }

        return report;
    }

    public async Task RefreshSourcesAsync()
    {
        await PerformHealthCheckAsync();
    }

    public void Dispose()
    {
        StopSmartMonitoring();
        _refreshLock.Dispose();
    }
}
