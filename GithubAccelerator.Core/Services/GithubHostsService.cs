using System.Diagnostics;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace GithubAccelerator.Services;

public class HostsSource
{
    public string Url { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Priority { get; set; }
    public bool IsHealthy { get; set; } = true;
    public long LastResponseTimeMs { get; set; }
    public DateTime? LastCheckTime { get; set; }
}

public class GithubHostsService
{
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    public static readonly HostsSource[] HostsSources = new[]
    {
        new HostsSource
        {
            Url = "https://hosts.gitcdn.top/hosts.txt",
            Name = "GitCDN.top",
            Description = "GitCDN.top提供的GitHub Hosts镜像，国内访问友好",
            Priority = 1
        },
        new HostsSource
        {
            Url = "https://gitlab.com/ineo6/hosts/-/raw/master/hosts",
            Name = "ineo6/hosts",
            Description = "ineo6提供的GitHub Hosts，稳定性高",
            Priority = 2
        }
    };

    public event Action<string>? OnLog;

    public HostsSource[] GetHostsSources() => HostsSources;

    public async Task<HostsSource[]> CheckSourcesHealthAsync()
    {
        var results = new HostsSource[HostsSources.Length];
        
        var tasks = HostsSources.Select(async (source, index) =>
        {
            var info = new HostsSource
            {
                Url = source.Url,
                Name = source.Name,
                Description = source.Description,
                Priority = source.Priority
            };

            try
            {
                var sw = Stopwatch.StartNew();
                using var response = await _httpClient.GetAsync(source.Url, HttpCompletionOption.ResponseHeadersRead);
                sw.Stop();

                info.IsHealthy = response.IsSuccessStatusCode;
                info.LastResponseTimeMs = sw.ElapsedMilliseconds;
                info.LastCheckTime = DateTime.Now;
            }
            catch
            {
                info.IsHealthy = false;
                info.LastResponseTimeMs = -1;
                info.LastCheckTime = DateTime.Now;
            }

            results[index] = info;
        });

        await Task.WhenAll(tasks);
        return results.OrderBy(s => s.Priority).ThenBy(s => s.LastResponseTimeMs).ToArray();
    }

    public async Task<string> FetchHostsAsync()
    {
        var orderedSources = HostsSources.OrderBy(s => s.Priority).ToArray();
        var triedSources = new List<string>();
        Exception? lastException = null;

        foreach (var source in orderedSources)
        {
            OnLog?.Invoke($"正在尝试: {source.Name}...");
            try
            {
                var hosts = await TryFetchFromSourceAsync(source);
                if (!string.IsNullOrEmpty(hosts))
                {
                    OnLog?.Invoke($"成功从 {source.Name} 获取 Hosts 数据 (耗时: {source.LastResponseTimeMs}ms)");
                    return hosts;
                }
            }
            catch (TaskCanceledException ex)
            {
                lastException = ex;
                OnLog?.Invoke($"⚠ {source.Name} 请求超时，尝试下一个源...");
                PenalizeSource(source.Url, PenaltyReason.Timeout);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                lastException = ex;
                OnLog?.Invoke($"⚠ {source.Name} 被限流(403)，尝试下一个源...");
                PenalizeSource(source.Url, PenaltyReason.RateLimited);
            }
            catch (HttpRequestException ex)
            {
                lastException = ex;
                OnLog?.Invoke($"⚠ {source.Name} 网络错误: {ex.Message}，尝试下一个源...");
                PenalizeSource(source.Url, PenaltyReason.NetworkError);
            }
            catch (Exception ex)
            {
                lastException = ex;
                OnLog?.Invoke($"⚠ {source.Name} 未知错误: {ex.Message}，尝试下一个源...");
                PenalizeSource(source.Url, PenaltyReason.Unknown);
            }
            
            triedSources.Add(source.Name);
        }

        var errorMsg = $"无法获取GitHub Hosts数据，已尝试: {string.Join(", ", triedSources)}";
        OnLog?.Invoke(errorMsg);
        throw new Exception("无法获取GitHub Hosts数据，请检查网络连接。", lastException);
    }

    private enum PenaltyReason
    {
        Timeout,
        RateLimited,
        NetworkError,
        DataInvalid,
        Unknown
    }

    private void PenalizeSource(string url, PenaltyReason reason)
    {
        var source = HostsSources.FirstOrDefault(s => s.Url == url);
        if (source == null) return;

        switch (reason)
        {
            case PenaltyReason.Timeout:
                source.Priority = Math.Min(99, source.Priority + 2);
                break;
            case PenaltyReason.RateLimited:
                source.Priority = Math.Min(99, source.Priority + 3);
                break;
            case PenaltyReason.NetworkError:
                source.Priority = Math.Min(99, source.Priority + 1);
                break;
            case PenaltyReason.DataInvalid:
                source.Priority = Math.Min(99, source.Priority + 2);
                break;
        }
        
        source.IsHealthy = false;
    }

    private async Task<string?> TryFetchFromSourceAsync(HostsSource source)
    {
        try
        {
            var sw = Stopwatch.StartNew();
            var response = await _httpClient.GetAsync(source.Url);
            sw.Stop();

            source.LastResponseTimeMs = sw.ElapsedMilliseconds;
            source.LastCheckTime = DateTime.Now;

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                if (ContainsGithubHosts(content))
                {
                    source.IsHealthy = true;
                    return content;
                }
            }
            source.IsHealthy = false;
        }
        catch
        {
            source.IsHealthy = false;
            source.LastResponseTimeMs = -1;
            source.LastCheckTime = DateTime.Now;
        }
        return null;
    }

    private bool ContainsGithubHosts(string content)
    {
        return content.Contains("github.com") &&
               content.Contains("raw.githubusercontent.com");
    }

    public List<(string ip, string domain)> ParseHosts(string hostsContent)
    {
        var result = new List<(string ip, string domain)>();
        var lines = hostsContent.Split('\n');

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith('#'))
                continue;

            var match = Regex.Match(trimmedLine, @"^(\d+\.\d+\.\d+\.\d+)\s+(\S+)$");
            if (match.Success)
            {
                var ip = match.Groups[1].Value;
                var domain = match.Groups[2].Value;
                result.Add((ip, domain));
            }
        }

        return result;
    }

    public DateTime? GetUpdateTime(string hostsContent)
    {
        var match = Regex.Match(hostsContent, @"Update[d]?[ -]time:\s*(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})");
        if (match.Success && DateTime.TryParse(match.Groups[1].Value, out var dateTime))
            return dateTime;
        return null;
    }

    public string GetSourcesSummary()
    {
        var lines = new List<string>();
        lines.Add("=== GitHub Hosts 加速源列表 ===");
        lines.Add("");

        foreach (var source in HostsSources.OrderBy(s => s.Priority))
        {
            var status = source.IsHealthy ? "✓" : "✗";
            var time = source.LastResponseTimeMs > 0 ? $"{source.LastResponseTimeMs}ms" : "未检测";
            lines.Add($"{status} [{source.Priority}] {source.Name}");
            lines.Add($"    URL: {source.Url}");
            lines.Add($"    描述: {source.Description}");
            lines.Add($"    响应时间: {time}");
            lines.Add("");
        }

        return string.Join("\n", lines);
    }
}
