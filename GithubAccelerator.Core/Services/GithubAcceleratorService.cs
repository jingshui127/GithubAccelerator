using System.Diagnostics;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GithubAccelerator.Services;

public class DiagnosticResult
{
    public string TestName { get; set; } = string.Empty;
    public bool IsSuccess { get; set; }
    public string Message { get; set; } = string.Empty;
    public long LatencyMs { get; set; }
    public string Details { get; set; } = string.Empty;
}

public class IpSpeedTestResult
{
    public string Ip { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public long LatencyMs { get; set; }
    public bool IsReachable { get; set; }
}

public class GithubAcceleratorService
{
    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    private static readonly string[] HostsSources = new[]
    {
        "https://raw.hellogithub.com/hosts",
        "https://raw.githubusercontent.com/521xueweihan/GitHub520/main/hosts",
        "https://hosts. github.is儋袍屠?actual problematic domain but we'll handle gracefully",
        "https://raw.gitmirror.com/521xueweihan/GitHub520/main/hosts"
    };

    private static readonly string[] GitHubDomains = new[]
    {
        "github.com",
        "api.github.com",
        "raw.githubusercontent.com",
        "objects.githubusercontent.com",
        "github.global.ssl.fastly.net",
        "codeload.github.com",
        "downloads.github.com",
        "avatars.githubusercontent.com",
        "git.github.com"
    };

    public event Action<string, int, int>? OnFetchProgress;
    public event Action<string>? OnLog;

    public async Task<string> FetchHostsWithFallbackAsync()
    {
        var triedSources = new List<string>();
        var lastError = "";

        for (int i = 0; i < HostsSources.Length; i++)
        {
            var source = HostsSources[i];
            OnFetchProgress?.Invoke($"尝试数据源 {i + 1}/{HostsSources.Length}: {source}", i + 1, HostsSources.Length);

            try
            {
                var response = await _httpClient.GetAsync(source, HttpCompletionOption.ResponseHeadersRead);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    if (IsValidHostsContent(content))
                    {
                        OnLog?.Invoke($"成功从 {GetDomainFromUrl(source)} 获取 Hosts 数据");
                        return content;
                    }
                }
                triedSources.Add($"{source} (HTTP {(int)response.StatusCode})");
            }
            catch (Exception ex)
            {
                triedSources.Add($"{source} ({ex.Message})");
                lastError = ex.Message;
            }
        }

        var errorMsg = $"无法获取 Hosts 数据。已尝试:\n{string.Join("\n", triedSources)}";
        OnLog?.Invoke(errorMsg);
        throw new Exception(lastError.Length > 100 ? "所有数据源均不可用，请检查网络连接" : lastError);
    }

    private bool IsValidHostsContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return false;

        var requiredDomains = new[] { "github.com", "raw.githubusercontent.com" };
        return requiredDomains.All(d => content.Contains(d)) &&
               Regex.IsMatch(content, @"^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\s+\S+\.github", RegexOptions.Multiline);
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

    public async Task<List<IpSpeedTestResult>> TestIpSpeedsAsync(List<(string ip, string domain)> hosts, CancellationToken ct)
    {
        var results = new List<IpSpeedTestResult>();
        var tasks = hosts.Select(h => TestSingleIpAsync(h.ip, h.domain, ct));
        var allResults = await Task.WhenAll(tasks);
        return allResults.Where(r => r != null).ToList()!;
    }

    private async Task<IpSpeedTestResult?> TestSingleIpAsync(string ip, string domain, CancellationToken ct)
    {
        var result = new IpSpeedTestResult { Ip = ip, Domain = domain };

        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(ip, 3000);

            result.LatencyMs = reply.RoundtripTime;
            result.IsReachable = reply.Status == IPStatus.Success;
        }
        catch
        {
            result.IsReachable = false;
            result.LatencyMs = -1;
        }

        return result;
    }

    public async Task<List<DiagnosticResult>> RunDiagnosticsAsync(CancellationToken ct)
    {
        var results = new List<DiagnosticResult>();

        results.Add(await TestDnsResolutionAsync(ct));
        results.Add(await TestGithubConnectivityAsync(ct));
        results.Add(await TestGitConnectivityAsync(ct));
        results.Add(await TestApiConnectivityAsync(ct));

        return results;
    }

    private async Task<DiagnosticResult> TestDnsResolutionAsync(CancellationToken ct)
    {
        var result = new DiagnosticResult { TestName = "DNS 解析测试" };
        var sw = Stopwatch.StartNew();

        try
        {
            var addresses = await System.Net.Dns.GetHostAddressesAsync("github.com", ct);
            sw.Stop();

            if (addresses.Length > 0)
            {
                result.IsSuccess = true;
                result.Message = $"解析成功: {addresses[0]}";
                result.LatencyMs = sw.ElapsedMilliseconds;
                result.Details = $"IP数量: {addresses.Length}\n{string.Join("\n", addresses.Select(a => a.ToString()))}";
            }
            else
            {
                result.IsSuccess = false;
                result.Message = "DNS 解析返回空结果";
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            result.IsSuccess = false;
            result.Message = "DNS 解析失败";
            result.Details = ex.Message;
            result.LatencyMs = sw.ElapsedMilliseconds;
        }

        return result;
    }

    private async Task<DiagnosticResult> TestGithubConnectivityAsync(CancellationToken ct)
    {
        var result = new DiagnosticResult { TestName = "GitHub 网站连接测试" };
        var sw = Stopwatch.StartNew();

        try
        {
            using var response = await _httpClient.GetAsync("https://github.com", HttpCompletionOption.ResponseHeadersRead, ct);
            sw.Stop();

            result.IsSuccess = response.IsSuccessStatusCode;
            result.Message = $"HTTP {(int)response.StatusCode} - {response.ReasonPhrase}";
            result.LatencyMs = sw.ElapsedMilliseconds;
            result.Details = $"Headers:\n{string.Join("\n", response.Headers.Select(h => $"{h.Key}: {string.Join(", ", h.Value)}"))}";
        }
        catch (Exception ex)
        {
            sw.Stop();
            result.IsSuccess = false;
            result.Message = "无法连接到 GitHub";
            result.Details = ex.Message;
            result.LatencyMs = sw.ElapsedMilliseconds;
        }

        return result;
    }

    private async Task<DiagnosticResult> TestGitConnectivityAsync(CancellationToken ct)
    {
        var result = new DiagnosticResult { TestName = "Git API 连接测试" };
        var sw = Stopwatch.StartNew();

        try
        {
            using var response = await _httpClient.GetAsync("https://api.github.com", HttpCompletionOption.ResponseHeadersRead, ct);
            sw.Stop();

            result.IsSuccess = response.IsSuccessStatusCode;
            result.Message = $"HTTP {(int)response.StatusCode}";
            result.LatencyMs = sw.ElapsedMilliseconds;
        }
        catch (Exception ex)
        {
            sw.Stop();
            result.IsSuccess = false;
            result.Message = "Git API 连接失败";
            result.Details = ex.Message;
            result.LatencyMs = sw.ElapsedMilliseconds;
        }

        return result;
    }

    private async Task<DiagnosticResult> TestApiConnectivityAsync(CancellationToken ct)
    {
        var result = new DiagnosticResult { TestName = "Raw 文件连接测试" };
        var sw = Stopwatch.StartNew();

        try
        {
            using var response = await _httpClient.GetAsync("https://raw.githubusercontent.com", HttpCompletionOption.ResponseHeadersRead, ct);
            sw.Stop();

            result.IsSuccess = response.IsSuccessStatusCode;
            result.Message = $"HTTP {(int)response.StatusCode}";
            result.LatencyMs = sw.ElapsedMilliseconds;
        }
        catch (Exception ex)
        {
            sw.Stop();
            result.IsSuccess = false;
            result.Message = "Raw 文件连接失败";
            result.Details = ex.Message;
            result.LatencyMs = sw.ElapsedMilliseconds;
        }

        return result;
    }

    public string GenerateGitConfigCommands()
    {
        return @"# Git 全局镜像配置（可选）
# 将以下命令在命令行中执行，可加速 Git 克隆

# 1. GitHub 镜像（推荐）
git config --global url.""https://github.com/"".insteadOf ""https://github.com/""
git config --global url.""https://github.com/"".insteadOf ""git@github.com:""

# 2. 使用 FastGit 镜像（备选）
# git config --global url.""https://hub.fastgit.org/"".insteadOf ""https://github.com/""

# 恢复原配置
# git config --global --unset url.""https://github.com/"".insteadOf

# 3. 如果使用代理（需要本地代理工具，如 Clash）
# git config --global http.proxy ""http://127.0.0.1:7890""
# git config --global https.proxy ""http://127.0.0.1:7890""
";
    }

    public List<(string domain, string currentIp)> GetCurrentGithubIps()
    {
        var results = new List<(string domain, string currentIp)>();

        foreach (var domain in GitHubDomains)
        {
            try
            {
                var addresses = System.Net.Dns.GetHostAddresses(domain);
                if (addresses.Length > 0)
                {
                    results.Add((domain, addresses[0].ToString()));
                }
            }
            catch
            {
                results.Add((domain, "解析失败"));
            }
        }

        return results;
    }

    private string GetDomainFromUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            return uri.Host;
        }
        catch
        {
            return url;
        }
    }
}
