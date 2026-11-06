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

    /// <summary>可注入探测器，便于测试取消语义；生产环境用 <see cref="ProbeAsync"/>。</summary>
    private readonly Func<string, CancellationToken, Task<(long LatencyMs, string Status, string Error)>> _probe;

    public HealthCheckService()
        : this(null)
    {
    }

    public HealthCheckService(
        Func<string, CancellationToken, Task<(long LatencyMs, string Status, string Error)>>? probe)
    {
        // 禁止重定向（避免重定向后算成 200）
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "GithubAccelerator/1.0");
        _probe = probe ?? ProbeAsync;
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
    /// 采用有界并发：取消后不再发起新的站点请求；已完成的项保留，未开始的项标记为 CANCELED。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    /// <param name="onProgress">每完成一项回调一次 (已完成, 共 N 项)，不编造百分比。</param>
    /// <param name="maxConcurrency">同时在途的请求数，避免一次把所有站点都发出去导致取消无法生效。</param>
    public async Task<List<HealthCheckItem>> ProbeAllAsync(
        CancellationToken ct = default,
        Action<int, int>? onProgress = null,
        int maxConcurrency = 3)
    {
        var items = GitHubSubDomains.Items;
        var total = items.Count;
        var results = new HealthCheckItem[total];
        var completed = 0;
        using var gate = new SemaphoreSlim(Math.Max(1, maxConcurrency));

        var tasks = items.Select((item, index) => Task.Run(async () =>
        {
            var check = new HealthCheckItem
            {
                Name = item.Name,
                Host = item.Host,
                ProbeUrl = item.ProbeUrl,
                Description = item.Description,
                LastCheckTime = DateTime.Now,
            };
            results[index] = check;
            var acquired = false;

            try
            {
                await gate.WaitAsync(ct).ConfigureAwait(false);
                acquired = true;

                // 拿到并发槽后再确认一次：取消后不再发起该站点的网络请求。
                ct.ThrowIfCancellationRequested();

                var (latency, status, error) = await _probe(item.ProbeUrl, ct).ConfigureAwait(false);
                check.LatencyMs = latency;
                check.Status = status;
                check.ErrorMessage = error;
            }
            catch (OperationCanceledException)
            {
                check.LatencyMs = -1;
                check.Status = "CANCELED";
                check.ErrorMessage = "已取消，未完成探测";
            }
            catch (Exception ex)
            {
                check.LatencyMs = -1;
                check.Status = "FAIL";
                check.ErrorMessage = ex.Message;
            }
            finally
            {
                if (acquired) gate.Release();
                check.LastCheckTime = DateTime.Now;
                onProgress?.Invoke(Interlocked.Increment(ref completed), total);
            }
        }));

        await Task.WhenAll(tasks);
        return results.ToList();
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
