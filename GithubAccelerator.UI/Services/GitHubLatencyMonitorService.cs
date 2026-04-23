using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace GithubAccelerator.UI.Services;

public class GitHubLatencyRecord
{
    public DateTime Timestamp { get; set; }
    public int LatencyMs { get; set; }
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
}

public class GitHubLatencyMonitorService : IDisposable
{
    private static readonly Lazy<GitHubLatencyMonitorService> _instance = 
        new(() => new GitHubLatencyMonitorService());
    
    public static GitHubLatencyMonitorService Instance => _instance.Value;

    private readonly List<GitHubLatencyRecord> _records = new();
    private readonly HttpClient _httpClient;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _monitorTask;
    private TimeSpan _checkInterval = TimeSpan.FromSeconds(5);
    private readonly int _maxRecords = 60;
    private bool _isMonitoring;
    private bool _disposed;

    public bool IsMonitoring => _isMonitoring;
    public IReadOnlyList<GitHubLatencyRecord> Records => _records.AsReadOnly();
    public int CurrentLatency { get; private set; }
    public int AverageLatency { get; private set; }
    public int MinLatency { get; private set; }
    public int MaxLatency { get; private set; }
    public double SuccessRate { get; private set; }

    public event Action? OnLatencyUpdated;

    public void SetCheckInterval(TimeSpan interval)
    {
        _checkInterval = interval;
    }

    public GitHubLatencyMonitorService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
    }

    public void StartMonitoring()
    {
        if (_isMonitoring) return;

        _isMonitoring = true;
        _cancellationTokenSource = new CancellationTokenSource();
        _monitorTask = MonitorLoopAsync(_cancellationTokenSource.Token);
    }

    public void StopMonitoring()
    {
        if (!_isMonitoring) return;

        _isMonitoring = false;
        _cancellationTokenSource?.Cancel();
        _monitorTask?.Wait(1000);
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
        _monitorTask = null;
    }

    private async Task MonitorLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await CheckGitHubLatencyAsync(cancellationToken);
            }
            catch (Exception)
            {
                // 忽略单次失败，继续监控
            }

            try
            {
                await Task.Delay(_checkInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task CheckGitHubLatencyAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var record = new GitHubLatencyRecord
        {
            Timestamp = DateTime.Now
        };

        // 使用多个测试地址，只要有一个成功即可
        var testUrls = new[]
        {
            "https://github.com/favicon.ico",
            "https://github.com/",
            "https://api.github.com/"
        };

        Exception? lastException = null;
        
        foreach (var url in testUrls)
        {
            try
            {
                stopwatch.Restart();
                var response = await _httpClient.GetAsync(
                    url, 
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                
                stopwatch.Stop();
                
                if (response.IsSuccessStatusCode || (int)response.StatusCode < 500)
                {
                    record.LatencyMs = (int)stopwatch.ElapsedMilliseconds;
                    record.IsSuccess = true;
                    record.ErrorMessage = null;
                    break;
                }
                else
                {
                    lastException = new Exception($"HTTP {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                lastException = ex;
                continue;
            }
        }

        if (!record.IsSuccess)
        {
            stopwatch.Stop();
            record.LatencyMs = -1;
            record.IsSuccess = false;
            record.ErrorMessage = lastException?.Message ?? "所有测试地址均无法访问";
        }

        lock (_records)
        {
            _records.Add(record);
            
            // 限制记录数量
            if (_records.Count > _maxRecords)
            {
                _records.RemoveAt(0);
            }

            // 更新统计数据
            UpdateStatistics();
        }

        OnLatencyUpdated?.Invoke();
    }

    private void UpdateStatistics()
    {
        if (_records.Count == 0)
        {
            CurrentLatency = 0;
            MinLatency = 0;
            MaxLatency = 0;
            AverageLatency = 0;
            SuccessRate = 0;
            return;
        }

        // 使用所有记录（包括失败的）来计算统计数据，但只统计成功的
        var successfulRecords = _records.FindAll(r => r.IsSuccess && r.LatencyMs >= 0);
        
        if (successfulRecords.Count > 0)
        {
            // 当前延迟：最后一个成功记录的延迟
            var lastSuccessful = successfulRecords[^1];
            CurrentLatency = lastSuccessful.LatencyMs;
            
            // 最小/最大/平均：只基于成功记录
            MinLatency = successfulRecords.Min(r => r.LatencyMs);
            MaxLatency = successfulRecords.Max(r => r.LatencyMs);
            AverageLatency = (int)successfulRecords.Average(r => r.LatencyMs);
        }
        else
        {
            // 如果都失败了，显示 0
            CurrentLatency = 0;
            MinLatency = 0;
            MaxLatency = 0;
            AverageLatency = 0;
        }

        // 成功率：成功记录数 / 总记录数
        SuccessRate = (double)successfulRecords.Count / _records.Count * 100;
    }

    public List<GitHubLatencyRecord> GetRecentRecords(int count = 30)
    {
        lock (_records)
        {
            if (_records.Count == 0) return new List<GitHubLatencyRecord>();
            var result = count >= _records.Count ? _records.ToList() : _records.GetRange(_records.Count - count, count);
            return result;
        }
    }

    public void ClearRecords()
    {
        lock (_records)
        {
            _records.Clear();
            CurrentLatency = 0;
            AverageLatency = 0;
            MinLatency = 0;
            MaxLatency = 0;
            SuccessRate = 0;
            OnLatencyUpdated?.Invoke();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        StopMonitoring();
        _httpClient.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
