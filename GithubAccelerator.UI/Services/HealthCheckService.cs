using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GithubAccelerator.UI.Models;

namespace GithubAccelerator.UI.Services;

/// <summary>
/// GitHub 关键子域健康检查服务。
/// 同时探测多个子域，找出"测速很快但浏览器打不开"这类局部问题。
/// </summary>
public sealed class HealthCheckService
{
    private static readonly Lazy<HealthCheckService> _lazy = new(() => new HealthCheckService());
    public static HealthCheckService Instance => _lazy.Value;

    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(12)
    };

    private HealthCheckService()
    {
        // 禁止重定向（避免重定向后算成 200）
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "GithubAccelerator/1.0");
    }

    /// <summary>
    /// 探测单个子域。
    /// </summary>
    /// <returns>延迟毫秒（-1 表示失败/超时）</returns>
    public async Task<(long LatencyMs, string Status, string Error)> ProbeAsync(string url, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            // 只取响应头，加快探测
            using var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            sw.Stop();
            var ms = sw.ElapsedMilliseconds;

            if (resp.IsSuccessStatusCode)
            {
                var status = ms switch
                {
                    < 300 => "OK",
                    < 1000 => "SLOW",
                    _ => "SLOW"
                };
                return (ms, status, string.Empty);
            }

            // 403/429 可能是 IP 被风控，仍视为可达
            if ((int)resp.StatusCode == 403 || (int)resp.StatusCode == 429)
            {
                return (ms, "SLOW", $"HTTP {(int)resp.StatusCode}（IP 可能被限速）");
            }

            // 404 也视为可达：服务存在但根路径无首页（如 objects.githubusercontent.com）
            if ((int)resp.StatusCode == 404)
            {
                var status = ms switch
                {
                    < 300 => "OK",
                    < 1000 => "SLOW",
                    _ => "SLOW"
                };
                return (ms, status, $"HTTP 404（根路径无内容，服务可达）");
            }

            return (ms, "FAIL", $"HTTP {(int)resp.StatusCode}");
        }
        catch (TaskCanceledException)
        {
            sw.Stop();
            return (-1, "TIMEOUT", "请求超时（>12s）");
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            return (-1, "FAIL", ex.Message);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return (-1, "FAIL", ex.Message);
        }
    }

    /// <summary>
    /// 探测全部 GitHub 关键子域。
    /// </summary>
    public async Task<List<HealthCheckItem>> ProbeAllAsync(CancellationToken ct = default)
    {
        var results = new List<HealthCheckItem>();
        var tasks = GitHubSubDomains.Items.Select(async item =>
        {
            var check = new HealthCheckItem
            {
                Name = item.Name,
                Host = item.Host,
                ProbeUrl = item.ProbeUrl,
                Description = item.Description,
                LastCheckTime = DateTime.Now,
            };

            try
            {
                var (latency, status, error) = await ProbeAsync(item.ProbeUrl, ct);
                check.LatencyMs = latency;
                check.Status = status;
                check.ErrorMessage = error;
            }
            catch (Exception ex)
            {
                check.LatencyMs = -1;
                check.Status = "FAIL";
                check.ErrorMessage = ex.Message;
            }

            check.LastCheckTime = DateTime.Now;
            results.Add(check);
        });

        await Task.WhenAll(tasks);
        // 按配置顺序返回
        return GitHubSubDomains.Items
            .Select((item, idx) => results.FirstOrDefault(r => r.Host == item.Host) ?? new HealthCheckItem { Name = item.Name, Host = item.Host })
            .ToList();
    }

    /// <summary>
    /// 汇总报告，用于星尘上报。
    /// </summary>
    public string Summarize(IEnumerable<HealthCheckItem> items)
    {
        var list = items.ToList();
        var ok = list.Count(x => x.Status == "OK");
        var slow = list.Count(x => x.Status == "SLOW");
        var fail = list.Count(x => x.Status == "TIMEOUT" || x.Status == "FAIL");
        var failedHosts = list.Where(x => x.Status == "TIMEOUT" || x.Status == "FAIL").Select(x => x.Host);
        return $"OK={ok} SLOW={slow} FAIL={fail} failed=[{string.Join(",", failedHosts)}]";
    }
}
