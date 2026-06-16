using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using Microsoft.Extensions.Logging;

namespace GithubAccelerator.Services;

/// <summary>
/// 通过多源 DNS 查询获取 GitHub 相关域名的可用 IP
/// 使用公共 DNS API（Google DoH、Cloudflare DoH、阿里DNS等）获取真实可用的 IP 地址
/// </summary>
public class ChinazDnsService : IDisposable
{
    private static readonly Lazy<ChinazDnsService> _instance = new(() => new ChinazDnsService());
    public static ChinazDnsService Instance => _instance.Value;

    // ===== 缓存机制 =====
    /// <summary>
    /// 缓存的有效期（5分钟）
    /// </summary>
    public static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 缓存的 hosts 内容
    /// </summary>
    private static string? _cachedHostsContent;

    /// <summary>
    /// 缓存生成时间
    /// </summary>
    private static DateTime _cacheGeneratedTime = DateTime.MinValue;

    /// <summary>
    /// 是否有可用缓存
    /// </summary>
    public static bool HasCachedContent => !string.IsNullOrEmpty(_cachedHostsContent)
        && DateTime.Now - _cacheGeneratedTime < CacheTtl;

    /// <summary>
    /// 获取缓存内容（可能为 null）
    /// </summary>
    public static string? CachedContent => HasCachedContent ? _cachedHostsContent : null;

    /// <summary>
    /// 获取缓存年龄描述
    /// </summary>
    public static string CacheAgeDescription
    {
        get
        {
            if (!HasCachedContent) return "无缓存";
            var age = DateTime.Now - _cacheGeneratedTime;
            if (age.TotalSeconds < 60) return $"{(int)age.TotalSeconds} 秒前";
            if (age.TotalMinutes < 60) return $"{(int)age.TotalMinutes} 分钟前";
            return $"{(int)age.TotalHours} 小时前";
        }
    }

    private readonly HttpClient _httpClient;
    private readonly ILogger<ChinazDnsService> _logger;
    private bool _disposed;

    // GitHub 相关的关键域名
    private static readonly string[] GitHubDomains = new[]
    {
        "github.com",
        "raw.githubusercontent.com",
        "objects.githubusercontent.com",
        "api.github.com"
    };

    // 多个公共 DNS 查询源（国内优先，超时短）
    private static readonly DnsProvider[] DnsProviders = new[]
    {
        // ===== 国内可访问的 DoH（优先）=====
        new DnsProvider { Name = "AliDNS", Url = "https://dns.alidns.com/dns-query", Type = "json", TimeoutSeconds = 5 },
        new DnsProvider { Name = "TencentDNS", Url = "https://doh.pub/dns-query", Type = "json", TimeoutSeconds = 5 },
        // ===== 海外 DoH（可能被墙，短超时快速跳过）=====
        new DnsProvider { Name = "Google", Url = "https://dns.google/resolve", Type = "json", TimeoutSeconds = 3 },
        new DnsProvider { Name = "Cloudflare", Url = "https://cloudflare-dns.com/dns-query", Type = "json", TimeoutSeconds = 3 }
    };

    // 已知的 GitHub 官方 IP 段（用于验证和过滤）
    private static readonly HashSet<string> KnownGitHubPrefixes = new()
    {
        "20.",      // 微软云/Azure - GitHub 主要托管
        "185.199",  // GitHub Pages/CDN
        "140.82",   // GitHub 核心 IP
        "192.30",
        "151.101",
        "185.199"
    };

    public ChinazDnsService() : this(LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<ChinazDnsService>())
    {
    }

    public ChinazDnsService(ILogger<ChinazDnsService> logger)
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "GitHubAccelerator/1.0");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/dns-json");
        _logger = logger;
    }

    /// <summary>
    /// 获取指定域名的可用 IP 列表（多源聚合，国内优先）
    /// </summary>
    public async Task<List<DnsResult>> GetAvailableIpsAsync(string domain)
    {
        var allIps = new List<(string Ip, string Source, int Priority)>();

        try
        {
            _logger.LogInformation("正在通过多源 DNS 查询 {Domain}", domain);

            // 1. 系统本地 DNS 解析（优先级最高，最快）
            var localIps = await ResolveFromLocalDnsAsync(domain);
            foreach (var ip in localIps)
            {
                if (!allIps.Any(x => x.Ip == ip))
                    allIps.Add((ip, "LocalDNS", 1));
            }

            // 2. 遍历所有 DoH 提供商（按配置顺序，国内优先）
            foreach (var provider in DnsProviders)
            {
                try
                {
                    var ips = await ResolveFromDoHAsync(domain, provider.Url, provider.TimeoutSeconds);
                    foreach (var ip in ips)
                    {
                        if (!allIps.Any(x => x.Ip == ip))
                            allIps.Add((ip, $"{provider.Name}-DoH", allIps.Count + 1));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "{Provider} 查询 {Domain} 跳过（超时/不可达）", provider.Name, domain);
                    // 被墙的源快速跳过，不影响其他源
                }
            }

            _logger.LogInformation("从多源 DNS 获取到 {Domain} 的 {Count} 个唯一 IP", domain, allIps.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询 {Domain} DNS 失败", domain);
        }

        return allIps.Select(x => new DnsResult
        {
            Domain = domain,
            Ip = x.Ip,
            Source = x.Source,
            QueryTime = DateTime.Now
        }).ToList();
    }

    /// <summary>
    /// 本地系统 DNS 解析
    /// </summary>
    private async Task<List<string>> ResolveFromLocalDnsAsync(string domain)
    {
        try
        {
            var ips = await Dns.GetHostAddressesAsync(domain);
            return ips
                .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork)
                .Select(ip => ip.ToString())
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "本地 DNS 解析 {Domain} 失败", domain);
            return new List<string>();
        }
    }

    /// <summary>
    /// 通过 DNS-over-HTTPS API 解析域名（支持独立超时）
    /// </summary>
    private async Task<List<string>> ResolveFromDoHAsync(string domain, string dohUrl, int timeoutSeconds = 5)
    {
        try
        {
            // 使用独立的短超时 HttpClient，避免影响全局设置
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            var url = $"{dohUrl}?name={HttpUtility.UrlEncode(domain)}&type=A";
            var httpResponse = await _httpClient.GetAsync(url, cts.Token);
            httpResponse.EnsureSuccessStatusCode();
            var response = await httpResponse.Content.ReadAsStringAsync();

            // Google/Cloudflare JSON 格式响应
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            if (root.TryGetProperty("Answer", out var answers))
            {
                var ips = new List<string>();
                foreach (var answer in answers.EnumerateArray())
                {
                    if (answer.TryGetProperty("data", out var data))
                    {
                        var ipStr = data.GetString();
                        if (IsValidIp(ipStr) && IsLikelyGitHubIp(ipStr))
                        {
                            ips.Add(ipStr!);
                        }
                    }
                }
                return ips;
            }

            return new List<string>();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "DoH 查询 {Url} 失败", dohUrl);
            return new List<string>();
        }
    }

    /// <summary>
    /// 验证是否为有效的 IPv4 地址且不是特殊地址
    /// </summary>
    private static bool IsValidIp(string? ip)
    {
        if (string.IsNullOrEmpty(ip)) return false;

        var parts = ip.Split('.');
        if (parts.Length != 4) return false;

        return parts.All(p => byte.TryParse(p, out var b) && b > 0)
               && !ip.StartsWith("0.")
               && !ip.StartsWith("127.")
               && ip != "8.8.8.8"
               && ip != "1.1.1.1";
    }

    /// <summary>
    /// 判断 IP 是否可能属于 GitHub（基于已知前缀）
    /// </summary>
    private static bool IsLikelyGitHubIp(string ip)
    {
        // 如果是已知的 GitHub IP 前缀，直接返回 true
        foreach (var prefix in KnownGitHubPrefixes)
        {
            if (ip.StartsWith(prefix)) return true;
        }

        // 对于未知前缀的 IP 也保留（可能是新的 CDN 节点）
        // 但排除明显不是的（如国内运营商 IP、私有地址等）
        var firstOctet = ip.Split('.')[0];
        var octetValue = int.Parse(firstOctet);

        // 排除私有地址段和一些明显不相关的
        return !(octetValue == 10 || octetValue == 172 && ip.StartsWith("172.16")
              || octetValue == 192 && ip.StartsWith("192.168"));
    }

    /// <summary>
    /// 获取所有 GitHub 相关域名的可用 IP（并发查询优化）
    /// </summary>
    public async Task<Dictionary<string, List<DnsResult>>> GetAllGitHubIpsAsync()
    {
        var allResults = new Dictionary<string, List<DnsResult>>();
        var tasks = GitHubDomains.Select(async domain =>
        {
            try
            {
                var ips = await GetAvailableIpsAsync(domain);
                return (domain, ips);
            }
            catch
            {
                return (domain, new List<DnsResult>());
            }
        });

        var results = await Task.WhenAll(tasks);
        foreach (var (domain, ips) in results)
        {
            if (ips.Any())
            {
                allResults[domain] = ips;
            }
        }

        return allResults;
    }

    /// <summary>
    /// 生成 hosts 格式的数据（带缓存，优先返回缓存）
    /// </summary>
    public async Task<string> GenerateHostsContentAsync()
    {
        // 优先使用缓存
        if (HasCachedContent)
        {
            _logger.LogInformation("从缓存读取 Hosts 内容（{Age}）", CacheAgeDescription);
            return _cachedHostsContent!;
        }

        // 缓存过期或不存在，重新查询
        _logger.LogInformation("缓存已过期/不存在，正在重新查询 DNS...");
        var content = await GenerateFreshHostsContentAsync();

        // 更新缓存
        UpdateCache(content);

        return content;
    }

    /// <summary>
    /// 强制刷新并生成新的 hosts 内容（不读缓存）
    /// </summary>
    public async Task<string> ForceRefreshHostsContentAsync()
    {
        _logger.LogInformation("强制刷新 DNS 探测数据...");
        var content = await GenerateFreshHostsContentAsync();
        UpdateCache(content);
        return content;
    }

    /// <summary>
    /// 更新缓存内容
    /// </summary>
    public static void UpdateCache(string content)
    {
        _cachedHostsContent = content;
        _cacheGeneratedTime = DateTime.Now;
    }

    /// <summary>
    /// 实际生成 hosts 内容的核心方法
    /// </summary>
    private async Task<string> GenerateFreshHostsContentAsync()
    {
        var allIps = await GetAllGitHubIpsAsync();
        var lines = new List<string>
        {
            "# ======================================================",
            "#  GitHub Hosts - 多源 DNS 探测生成",
            $"#  生成时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
            "#  数据来源: LocalDNS / Google DoH / Cloudflare DoH / AliDNS",
            "# ======================================================",
            ""
        };

        // github.com
        lines.Add("# --- github.com ---");
        if (allIps.TryGetValue("github.com", out var githubIps) && githubIps.Any())
        {
            var bestIps = githubIps.Take(3).ToList();
            foreach (var ip in bestIps)
            {
                lines.Add($"{ip.Ip,-18} github.com          # [{ip.Source}]");
                lines.Add($"{ip.Ip,-18} www.github.com       # [{ip.Source}]");
            }
        }
        else
        {
            lines.Add("# (未能获取到可用 IP)");
        }

        lines.Add("");
        lines.Add("# --- raw.githubusercontent.com (GitHub 文件下载) ---");
        if (allIps.TryGetValue("raw.githubusercontent.com", out var rawIps) && rawIps.Any())
        {
            foreach (var ip in rawIps.Take(5))
            {
                lines.Add($"{ip.Ip,-18} raw.githubusercontent.com   # [{ip.Source}]");
            }
        }

        lines.Add("");
        lines.Add("# --- objects.githubusercontent.com (GitHub LFS 对象) ---");
        if (allIps.TryGetValue("objects.githubusercontent.com", out var objIps) && objIps.Any())
        {
            foreach (var ip in objIps.Take(5))
            {
                lines.Add($"{ip.Ip,-18} objects.githubusercontent.com # [{ip.Source}]");
            }
        }

        lines.Add("");
        lines.Add("# --- api.github.com (GitHub API) ---");
        if (allIps.TryGetValue("api.github.com", out var apiIps) && apiIps.Any())
        {
            foreach (var ip in apiIps.Take(3))
            {
                lines.Add($"{ip.Ip,-18} api.github.com       # [{ip.Source}]");
            }
        }

        lines.Add("");

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// 验证 IP 是否可通过 HTTPS 访问
    /// </summary>
    public async Task<bool> ValidateIpAsync(string ip, string domain)
    {
        try
        {
            using var tcpClient = new TcpClient();
            var connectTask = tcpClient.ConnectAsync(ip, 443);
            var timeoutTask = Task.Delay(3000);

            var completedTask = await Task.WhenAny(connectTask, timeoutTask);

            if (completedTask == connectTask && tcpClient.Connected)
            {
                _logger.LogInformation("IP {Ip} 可以连接到 {Domain}", ip, domain);
                tcpClient.Close();
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "IP {Ip} 连接 {Domain} 失败", ip, domain);
        }

        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _httpClient.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// DNS 提供商配置
/// </summary>
public class DnsProvider
{
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Type { get; set; } = "json";
    public int TimeoutSeconds { get; set; } = 5;
}

/// <summary>
/// DNS 查询结果
/// </summary>
public class DnsResult
{
    public string Domain { get; set; } = string.Empty;
    public string Ip { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public DateTime QueryTime { get; set; }
}
