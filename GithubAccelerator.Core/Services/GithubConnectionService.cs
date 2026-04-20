using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GithubAccelerator.Services;

public class ConnectionTestResult
{
    public string Endpoint { get; set; } = string.Empty;
    public bool IsSuccess { get; set; }
    public long LatencyMs { get; set; }
    public string StatusCode { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public DateTime TestTime { get; set; } = DateTime.Now;
}

public class GithubConnectionService
{
    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private static readonly string[] TestEndpoints =
    {
        "https://api.github.com",
        "https://github.com",
        "https://raw.githubusercontent.com"
    };

    public event Action<string, bool>? OnStatusUpdate;
    public event Action<ConnectionTestResult>? OnTestComplete;
    public event Action<int, int>? OnProgressUpdate;
    public event Action<string>? OnLog;

    public async Task<List<ConnectionTestResult>> TestAllConnectionsAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<ConnectionTestResult>();
        var total = TestEndpoints.Length;
        var current = 0;

        OnLog?.Invoke($"[{DateTime.Now:HH:mm:ss}] 开始测试 GitHub 连接...");
        OnLog?.Invoke($"[{DateTime.Now:HH:mm:ss}] 测试时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        foreach (var endpoint in TestEndpoints)
        {
            current++;
            OnProgressUpdate?.Invoke(current, total);
            OnStatusUpdate?.Invoke($"正在测试: {GetShortEndpoint(endpoint)}", true);

            var result = await TestEndpointAsync(endpoint, cancellationToken);
            results.Add(result);
            OnTestComplete?.Invoke(result);

            if (cancellationToken.IsCancellationRequested)
                break;
        }

        OnLog?.Invoke($"[{DateTime.Now:HH:mm:ss}] 测试完成，共 {results.Count} 项");
        return results;
    }

    private string GetShortEndpoint(string url)
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

    public async Task<ConnectionTestResult> TestEndpointAsync(string url, CancellationToken cancellationToken = default)
    {
        var result = new ConnectionTestResult
        {
            Endpoint = url,
            TestTime = DateTime.Now
        };
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            stopwatch.Stop();

            result.IsSuccess = response.IsSuccessStatusCode;
            result.LatencyMs = stopwatch.ElapsedMilliseconds;
            result.StatusCode = $"{(int)response.StatusCode}";

            OnLog?.Invoke($"[{DateTime.Now:HH:mm:ss}] {GetShortEndpoint(url)}: {(int)response.StatusCode} ({stopwatch.ElapsedMilliseconds}ms)");
        }
        catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            result.IsSuccess = false;
            result.ErrorMessage = "测试已取消";
            result.LatencyMs = stopwatch.ElapsedMilliseconds;
            OnLog?.Invoke($"[{DateTime.Now:HH:mm:ss}] {GetShortEndpoint(url)}: 已取消");
        }
        catch (HttpRequestException ex)
        {
            result.IsSuccess = false;
            result.ErrorMessage = ex.Message;
            result.LatencyMs = stopwatch.ElapsedMilliseconds;
            OnLog?.Invoke($"[{DateTime.Now:HH:mm:ss}] {GetShortEndpoint(url)}: 失败 - {ex.Message}");
        }
        catch (Exception ex)
        {
            result.IsSuccess = false;
            result.ErrorMessage = ex.Message;
            result.LatencyMs = stopwatch.ElapsedMilliseconds;
            OnLog?.Invoke($"[{DateTime.Now:HH:mm:ss}] {GetShortEndpoint(url)}: 错误 - {ex.Message}");
        }

        return result;
    }

    public async Task<(bool isSuccess, string message, string? token)> TestGitHubApiAsync(string? token = null, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com");
            request.Headers.UserAgent.ParseAdd("GithubAccelerator/1.0");

            if (!string.IsNullOrEmpty(token))
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            }

            using var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return (true, "GitHub API 连接成功！", null);
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return (false, "Token 无效或已过期", null);
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                return (false, "访问被限制，请检查 API 限额或 Token 权限", null);
            }
            else
            {
                return (false, $"API 返回错误: {(int)response.StatusCode}", null);
            }
        }
        catch (Exception ex)
        {
            return (false, $"连接失败: {ex.Message}", null);
        }
    }

    public async Task<(long latency, string ip, bool success)> TestGitHubLatencyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();

            using var request = new HttpRequestMessage(HttpMethod.Head, "https://github.com");
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            stopwatch.Stop();

            var ip = GetGitHubIp();
            return (stopwatch.ElapsedMilliseconds, ip, true);
        }
        catch (Exception ex)
        {
            OnLog?.Invoke($"[{DateTime.Now:HH:mm:ss}] 延迟测试失败: {ex.Message}");
            return (-1, "无法获取", false);
        }
    }

    private string GetGitHubIp()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "nslookup",
                Arguments = "github.com",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            };

            using var process = Process.Start(psi);
            if (process == null) return "未知";

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            var match = Regex.Match(output, @"Address:\s*(\d+\.\d+\.\d+\.\d+)$", RegexOptions.Multiline);
            return match.Success ? match.Groups[1].Value : "未知";
        }
        catch
        {
            return "未知";
        }
    }

    public string GenerateDetailedReport(List<ConnectionTestResult> results, long latency, string ip)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== GitHub 连接测试报告 ===");
        sb.AppendLine($"测试时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"本地IP: {ip}");
        sb.AppendLine();
        sb.AppendLine("--- 连接测试结果 ---");

        var successCount = results.Count(r => r.IsSuccess);
        var failCount = results.Count(r => !r.IsSuccess);

        foreach (var r in results)
        {
            var status = r.IsSuccess ? "[OK]" : "[FAIL]";
            var endpoint = GetShortEndpoint(r.Endpoint);
            if (r.IsSuccess)
            {
                sb.AppendLine($"{status} {endpoint,-30} {r.LatencyMs,5}ms  HTTP {r.StatusCode}");
            }
            else
            {
                sb.AppendLine($"{status} {endpoint,-30} Error: {r.ErrorMessage}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("--- 汇总 ---");
        sb.AppendLine($"成功: {successCount}/{results.Count}");
        sb.AppendLine($"失败: {failCount}/{results.Count}");
        sb.AppendLine($"延迟: {latency}ms");

        if (failCount > 0)
        {
            sb.AppendLine();
            sb.AppendLine("--- 失败详情 ---");
            foreach (var r in results.Where(x => !x.IsSuccess))
            {
                sb.AppendLine($"Endpoint: {r.Endpoint}");
                sb.AppendLine($"Error: {r.ErrorMessage}");
                sb.AppendLine($"Time: {r.TestTime:HH:mm:ss}");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }
}
