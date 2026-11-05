using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using NewLife.Log;

namespace GithubAccelerator.Services;

/// <summary>
/// Hosts IP 优化服务。
/// 在应用 hosts 之前，对每个子域的候选 IP 进行 HTTP(S) 探测，
/// 选择当前网络环境下实际可达且延迟最低的 IP，生成个性化 hosts。
/// </summary>
public class HostsOptimizerService
{
    private readonly HttpClient _httpClient;
    private readonly ChinazDnsService _dnsService;

    /// <summary>探测超时时间（秒）</summary>
    public int ProbeTimeoutSeconds { get; set; } = 5;

    /// <summary>每个域名最多保留几条映射</summary>
    public int MaxMappingsPerDomain { get; set; } = 1;

    /// <summary>是否同时保留 hosts 源中原有的 IP 作为候选</summary>
    public bool IncludeOriginalIps { get; set; } = true;

    /// <summary>是否同时通过 DNS 查询获取候选 IP（仅对核心 GitHub 域名生效）</summary>
    public bool IncludeDnsResolvedIps { get; set; } = true;

    /// <summary>核心域名列表：DNS 解析和多重验证仅针对这些域名</summary>
    private static readonly HashSet<string> CoreDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "github.com",
        "api.github.com",
        "raw.githubusercontent.com",
        "objects.githubusercontent.com",
        "avatars.githubusercontent.com",
        "gist.github.com",
        "github.io",
        "codeload.github.com",
        "github.githubassets.com",
        "media.githubusercontent.com",
        "user-images.githubusercontent.com",
    };

    /// <summary>探针并发度上限</summary>
    private const int MaxConcurrentProbes = 12;

    public event Action<string>? OnLog;

    public HostsOptimizerService()
    {
        _dnsService = ChinazDnsService.Instance;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(ProbeTimeoutSeconds)
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "GitHubAccelerator/1.5");
    }

    private void Log(string msg)
    {
        XTrace.WriteLine($"[HostsOptimizer] {msg}");
        OnLog?.Invoke(msg);
    }

    /// <summary>
    /// 优化 hosts 内容：对每个子域探测候选 IP，选择最佳 IP 重新生成 hosts。
    /// </summary>
    public async Task<string> OptimizeAsync(string originalHostsContent, CancellationToken ct = default)
    {
        Log($"开始优化，原始内容长度={originalHostsContent.Length}");
        var stopwatch = Stopwatch.StartNew();

        // 1. 解析原始 hosts 中的域名-IP映射
        var originalMappings = ParseHostsMappings(originalHostsContent);
        Log($"解析到原始映射 {originalMappings.Count} 条，涉及 {originalMappings.Select(m => m.Domain).Distinct().Count()} 个域名");

        if (originalMappings.Count == 0)
        {
            Log("未解析到任何 GitHub 域名映射，跳过优化");
            return originalHostsContent;
        }

        // 2. 按域名分组，收集候选 IP
        var domainCandidates = await CollectCandidatesAsync(originalMappings, ct);
        Log($"收集到候选 IP：{domainCandidates.Values.Sum(v => v.Count)} 个（涉及 {domainCandidates.Count} 个域名）");

        // 3. 对每个域名的候选 IP 进行 HTTP(S) 探测
        var optimizedMappings = await ProbeAndSelectBestAsync(domainCandidates, ct);
        stopwatch.Stop();

        var reachableCount = optimizedMappings.Count(m => m.IsReachable);
        Log($"优化完成：{reachableCount}/{optimizedMappings.Count} 个域名找到可达 IP，耗时 {stopwatch.ElapsedMilliseconds}ms");

        // 仅打印警告和错误：不可达或被限流的域名
        foreach (var m in optimizedMappings.Where(m => !m.IsReachable || m.IsRateLimited))
        {
            Log($"  {m.Domain,-35} -> {m.Ip,-16} 可达={m.IsReachable} 限流={m.IsRateLimited} 延迟={m.LatencyMs}ms 来源={m.Source} {(string.IsNullOrEmpty(m.ErrorMessage) ? "" : "错误=" + m.ErrorMessage)}");
        }

        // 4. 生成优化后的 hosts 内容
        var result = GenerateOptimizedHosts(optimizedMappings, originalHostsContent);
        Log($"生成优化后 hosts 内容长度={result.Length}");
        return result;
    }

    /// <summary>
    /// 解析 hosts 内容中的 IP-域名映射（只解析 GitHub 相关域名）。
    /// </summary>
    private List<HostMapping> ParseHostsMappings(string content)
    {
        var result = new List<HostMapping>();
        var lines = content.Split('\n');

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                continue;

            var match = Regex.Match(trimmed, @"^(\d+\.\d+\.\d+\.\d+)\s+(\S+)");
            if (match.Success)
            {
                var ip = match.Groups[1].Value;
                var domain = match.Groups[2].Value;

                // 只保留 GitHub 相关域名
                if (IsGitHubDomain(domain))
                {
                    result.Add(new HostMapping { Ip = ip, Domain = domain, Source = "original" });
                }
            }
        }

        return result;
    }

    /// <summary>
    /// 按域名收集候选 IP（原始 hosts 中的 + DNS 解析的）。
    /// </summary>
    private async Task<Dictionary<string, List<CandidateIp>>> CollectCandidatesAsync(
        List<HostMapping> originalMappings, CancellationToken ct)
    {
        var candidates = new Dictionary<string, List<CandidateIp>>(StringComparer.OrdinalIgnoreCase);

        // 按域名分组
        var domainGroups = originalMappings.GroupBy(m => m.Domain).ToList();

        // 并行处理每个域名
        var tasks = domainGroups.Select(async group =>
        {
            var domain = group.Key;
            var list = new List<CandidateIp>();

            // 加入原始 hosts 中的 IP
            if (IncludeOriginalIps)
            {
                foreach (var mapping in group)
                {
                    if (!list.Any(c => c.Ip == mapping.Ip))
                    {
                        list.Add(new CandidateIp { Ip = mapping.Ip, Domain = domain, Source = "original" });
                    }
                }
            }

            // 通过 DNS 查询获取更多候选 IP（仅核心域名）
            if (IncludeDnsResolvedIps && CoreDomains.Contains(domain))
            {
                try
                {
                    var dnsResults = await _dnsService.GetAvailableIpsAsync(domain);
                    foreach (var dns in dnsResults)
                    {
                        if (!list.Any(c => c.Ip == dns.Ip))
                        {
                            list.Add(new CandidateIp { Ip = dns.Ip, Domain = domain, Source = $"dns:{dns.Source}" });
                        }
                    }
                }
                catch (Exception ex)
                {
                    OnLog?.Invoke($"DNS 查询 {domain} 失败: {ex.Message}");
                }
            }

            return (domain, list);
        });

        var results = await Task.WhenAll(tasks);
        foreach (var (domain, list) in results)
        {
            if (list.Count > 0)
            {
                candidates[domain] = list;
            }
        }

        return candidates;
    }

    /// <summary>
    /// 对每个域名的候选 IP 进行 HTTP(S) 探测，选择最佳 IP。
    /// 核心域名：每个 IP 探测 3 次，要求至少 2 次成功，过滤间歇性封锁 IP。
    /// 非核心域名：探测 1 次即可。
    /// </summary>
    private async Task<List<OptimizedMapping>> ProbeAndSelectBestAsync(
        Dictionary<string, List<CandidateIp>> domainCandidates, CancellationToken ct)
    {
        var optimized = new List<OptimizedMapping>();

        var semaphore = new SemaphoreSlim(MaxConcurrentProbes);
        var allProbes = new List<(string Domain, string Ip, string Source, int Attempt, Task<ProbeResult> Task)>();

        foreach (var kvp in domainCandidates)
        {
            bool isCore = CoreDomains.Contains(kvp.Key);
            int attempts = isCore ? 3 : 1;

            foreach (var candidate in kvp.Value)
            {
                for (int a = 0; a < attempts; a++)
                {
                    allProbes.Add((kvp.Key, candidate.Ip, candidate.Source, a,
                        Task.Run(async () =>
                        {
                            await semaphore.WaitAsync(ct);
                            try
                            {
                                return await ProbeSingleIpAsync(candidate, ct);
                            }
                            finally
                            {
                                semaphore.Release();
                            }
                        })));
                }
            }
        }

        await Task.WhenAll(allProbes.Select(p => p.Task));

        // 按 (Domain, Ip) 聚合探针结果
        var grouped = allProbes
            .GroupBy(p => (p.Domain, p.Ip))
            .Select(g =>
            {
                var results = g.Select(p => p.Task.Result).ToList();
                var successCount = results.Count(r => r.IsReachable);
                var requiredSuccesses = CoreDomains.Contains(g.Key.Domain) ? 2 : 1;
                var isReachable = successCount >= requiredSuccesses;

                // 取成功探针中延迟最低的
                var best = results
                    .Where(r => r.IsReachable)
                    .OrderBy(r => r.LatencyMs)
                    .FirstOrDefault() ?? results.First();

                return new
                {
                    Domain = g.Key.Domain,
                    Ip = g.Key.Ip,
                    Source = best.Source,
                    IsReachable = isReachable,
                    BestLatency = best.LatencyMs,
                    HttpStatusCode = best.HttpStatusCode,
                    IsRateLimited = best.IsRateLimited,
                    ErrorMessage = isReachable ? string.Empty
                        : $"{successCount}/{results.Count} 次探针成功"
                };
            })
            .ToList();

        // 按域名选择最佳 IP
        foreach (var domain in domainCandidates.Keys)
        {
            var domainResults = grouped
                .Where(r => r.Domain.Equals(domain, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var best = domainResults
                .OrderByDescending(r => r.IsReachable)
                .ThenBy(r => r.BestLatency < 0 ? int.MaxValue : r.BestLatency)
                .FirstOrDefault();

            if (best != null)
            {
                optimized.Add(new OptimizedMapping
                {
                    Domain = domain,
                    Ip = best.Ip,
                    LatencyMs = best.BestLatency,
                    IsReachable = best.IsReachable,
                    IsRateLimited = best.IsRateLimited,
                    HttpStatusCode = best.HttpStatusCode,
                    Source = best.Source,
                    ErrorMessage = best.ErrorMessage
                });
            }
        }

        return optimized;
    }

    /// <summary>
    /// 对单个候选 IP 进行 HTTP(S) 探测。
    /// 使用 SocketsHttpHandler.ConnectCallback 强制连接到指定 IP，绕过 DNS 解析。
    /// </summary>
    private async Task<ProbeResult> ProbeSingleIpAsync(CandidateIp candidate, CancellationToken ct)
    {
        var result = new ProbeResult
        {
            Ip = candidate.Ip,
            Domain = candidate.Domain,
            Source = candidate.Source,
            LatencyMs = -1,
            IsReachable = false
        };

        var sw = Stopwatch.StartNew();

        try
        {
            // 使用 SocketsHttpHandler 强制连接到指定 IP
            using var handler = new SocketsHttpHandler
            {
                ConnectCallback = async (context, cancellationToken) =>
                {
                    var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    try
                    {
                        await socket.ConnectAsync(new IPEndPoint(IPAddress.Parse(candidate.Ip), 443), cancellationToken);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                },
                SslOptions = new SslClientAuthenticationOptions
                {
                    // 拒绝自签名/中间人证书，但允许 CDN IP 共享导致的 SNI 名称不匹配
                    RemoteCertificateValidationCallback = (sender, cert, chain, errors) =>
                        errors == SslPolicyErrors.None || !errors.HasFlag(SslPolicyErrors.RemoteCertificateChainErrors),
                    TargetHost = candidate.Domain
                },
                AllowAutoRedirect = false
            };

            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(ProbeTimeoutSeconds)
            };

            // 构造请求：访问根路径即可，大多数服务会返回 301/200/404
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://{candidate.Domain}/");
            request.Headers.Host = candidate.Domain;

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            sw.Stop();

            result.LatencyMs = sw.ElapsedMilliseconds;
            result.HttpStatusCode = (int)response.StatusCode;

            // 200/301/302/307/308 视为可达
            // 403/429 视为可达但被限流
            // 404 也视为可达（服务存在，只是路径不存在）
            var status = (int)response.StatusCode;
            result.IsReachable = status is >= 200 and < 400 or 404;
            result.IsRateLimited = status is 403 or 429;

            if (result.IsReachable)
            {
                result.ErrorMessage = string.Empty;
            }
            else
            {
                result.ErrorMessage = $"HTTP {status}";
            }
        }
        catch (TaskCanceledException)
        {
            sw.Stop();
            result.LatencyMs = sw.ElapsedMilliseconds;
            result.ErrorMessage = "请求超时";
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            result.LatencyMs = sw.ElapsedMilliseconds;
            result.ErrorMessage = ex.Message;
        }
        catch (SocketException ex)
        {
            sw.Stop();
            result.LatencyMs = sw.ElapsedMilliseconds;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// 生成优化后的 hosts 内容。
    /// </summary>
    private string GenerateOptimizedHosts(List<OptimizedMapping> optimized, string originalContent)
    {
        var sb = new StringBuilder();

        // 保留原始头部注释
        var headerLines = originalContent.Split('\n')
            .TakeWhile(l => l.Trim().StartsWith('#') || string.IsNullOrWhiteSpace(l));
        foreach (var line in headerLines)
        {
            sb.AppendLine(line);
        }

        sb.AppendLine("# ==============================================================");
        sb.AppendLine($"#  优化时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"#  原始映射: {optimized.Count} 个域名");
        sb.AppendLine($"#  可达域名: {optimized.Count(m => m.IsReachable)} 个");
        sb.AppendLine("#  探测方式: 强制直连 IP + HTTPS 握手 + HTTP 响应验证");
        sb.AppendLine("# ==============================================================");
        sb.AppendLine();

        // 按可达性分组：可达的在前，不可达的在后（写入兜底 IP 而非注释）
        foreach (var mapping in optimized.Where(m => m.IsReachable).OrderBy(m => m.Domain))
        {
            var status = mapping.IsRateLimited ? " [限流]" : string.Empty;
            sb.AppendLine($"{mapping.Ip,-20} {mapping.Domain,-35} # {mapping.LatencyMs}ms {mapping.Source}{status}");
        }

        var unreachable = optimized.Where(m => !m.IsReachable).ToList();
        if (unreachable.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("# ---- 以下域名探测不可达，写入兜底 IP（连通性不保证）----");
            foreach (var mapping in unreachable.OrderBy(m => m.Domain))
            {
                sb.AppendLine($"{mapping.Ip,-20} {mapping.Domain,-35} # [不保证] {mapping.ErrorMessage} [{mapping.Source}]");
            }
        }

        return sb.ToString().TrimEnd() + "\n";
    }

    private static bool IsGitHubDomain(string domain)
    {
        var lower = domain.ToLowerInvariant();
        return lower.Contains("github") || lower.Contains("githubusercontent") || lower.Contains("githubassets");
    }

    /// <summary>原始 hosts 映射</summary>
    private class HostMapping
    {
        public string Ip { get; set; } = string.Empty;
        public string Domain { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
    }

    /// <summary>候选 IP</summary>
    private class CandidateIp
    {
        public string Ip { get; set; } = string.Empty;
        public string Domain { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
    }

    /// <summary>探测结果</summary>
    private class ProbeResult
    {
        public string Ip { get; set; } = string.Empty;
        public string Domain { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public long LatencyMs { get; set; } = -1;
        public bool IsReachable { get; set; }
        public bool IsRateLimited { get; set; }
        public int HttpStatusCode { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }

    /// <summary>优化后的映射</summary>
    private class OptimizedMapping
    {
        public string Domain { get; set; } = string.Empty;
        public string Ip { get; set; } = string.Empty;
        public long LatencyMs { get; set; } = -1;
        public bool IsReachable { get; set; }
        public bool IsRateLimited { get; set; }
        public int HttpStatusCode { get; set; }
        public string Source { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
    }
}
