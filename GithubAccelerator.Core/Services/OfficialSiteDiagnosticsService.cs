using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text.Json;

namespace GithubAccelerator.Core.Services;

// ============================================================================
// M1「官网访问诊断」：纯只读观测。
//
// 边界（写在这里，也写在界面上）：
//   * 不写 Hosts、不请求管理员权限、不改 DNS / 代理 / 防火墙、不装证书；
//   * 不做后台常驻轮询，不自动打开浏览器；
//   * 本文件只允许 TCP / TLS / DoH / 系统 DNS 读取，不得引用任何写系统配置的服务
//     （HostsFileManager、HostsOptimizerService、GitUrlRewriteService 等）。
//   * 结论只回答“解析层是不是瓶颈”，不承诺一定能加速。
// ============================================================================

/// <summary>一个 DoH 端点。UrlTemplate 中的 {domain} 会被替换为目标域名。</summary>
public sealed record DohEndpoint(string Name, string UrlTemplate);

/// <summary>DoH JSON 解析结果：只接受 A（IPv4）记录。</summary>
public sealed record DohParseResult(IReadOnlyList<string> Addresses, string? Error);

/// <summary>单次 DoH 查询的实测结果（失败必须带原因）。</summary>
public sealed record DohQueryOutcome(
    string Domain,
    string SourceName,
    bool Success,
    IReadOnlyList<string> Addresses,
    long ElapsedMs,
    string? FailureReason);

/// <summary>一次 TCP+TLS 采样的结果；<see cref="FailureReason"/> 为 null 表示成功。</summary>
public sealed record HandshakeSample(int? ElapsedMs, string? FailureReason)
{
    public bool Success => FailureReason == null && ElapsedMs.HasValue;
}

/// <summary>系统 DNS（OS 解析）结果，作为基线来源。</summary>
public sealed record SystemResolution(
    IReadOnlyList<string> Ipv4Addresses,
    long ElapsedMs,
    string? FailureReason);

public interface IDohClient
{
    Task<DohQueryOutcome> QueryAsync(DohEndpoint endpoint, string domain, CancellationToken cancellationToken);
}

public interface IHandshakeProbe
{
    /// <summary>对指定 IP 的 443 端口做 TCP 连接 + TLS 握手（SNI = domain）。</summary>
    Task<HandshakeSample> MeasureAsync(string domain, string ip, TimeSpan timeout, CancellationToken cancellationToken);
}

public interface ISystemDnsResolver
{
    Task<SystemResolution> ResolveAsync(string domain, CancellationToken cancellationToken);
}

/// <summary>候选与基线的分类（20% 以上差异才算“更快/更慢”，否则视为相近）。</summary>
public enum BaselineComparison
{
    None = 0,
    Faster = 1,
    Similar = 2,
    Slower = 3
}

/// <summary>结论类别：决定界面显示哪种提示，是 M1 的主要产出。</summary>
public enum DiagnosticsConclusionKind
{
    Inconclusive = 0,
    OptimizationPossible = 1,
    NoExpectedGain = 2
}

/// <summary>表格中的一行（候选或基线）。</summary>
public sealed record IpDiagnosticsRow(
    string Domain,
    string Ip,
    string Source,
    bool IsBaseline,
    int? MedianMs,
    int SuccessSamples,
    int SampleCount,
    bool Available,
    string? FailureReason,
    BaselineComparison Comparison)
{
    public string AvailabilityText
    {
        get
        {
            if (!Available)
            {
                return string.IsNullOrWhiteSpace(FailureReason) ? "不可用" : $"不可用：{FailureReason}";
            }

            return SuccessSamples < SampleCount ? $"可用（{SuccessSamples}/{SampleCount} 次成功）" : "可用";
        }
    }

    public string ComparisonText => Comparison switch
    {
        BaselineComparison.Faster => "更快",
        BaselineComparison.Slower => "更慢",
        BaselineComparison.Similar => "相近",
        _ => "—"
    };
}

/// <summary>单个域名的 DoH 结果、候选/基线行与基线耗时。</summary>
public sealed record DomainDiagnostics(
    string Domain,
    IReadOnlyList<DohQueryOutcome> DohResults,
    IReadOnlyList<IpDiagnosticsRow> Rows,
    int? BaselineMedianMs,
    string? BaselineFailureReason)
{
    public IReadOnlyList<IpDiagnosticsRow> Candidates => Rows.Where(r => !r.IsBaseline).ToList();

    public bool DirectUsable => Rows.Any(r => r.IsBaseline && r.Available);

    public IpDiagnosticsRow? FastestAvailableCandidate => Candidates
        .Where(r => r.Available)
        .OrderBy(r => r.MedianMs)
        .FirstOrDefault();
}

/// <summary>一次完整诊断的结果。字段全部来自实测，不做估算。</summary>
public sealed record OfficialSiteDiagnosticsReport(
    DateTimeOffset CheckedAt,
    long TotalElapsedMs,
    long ResolutionElapsedMs,
    IReadOnlyList<DomainDiagnostics> Domains,
    DiagnosticsConclusionKind ConclusionKind,
    string ConclusionTitle,
    string ConclusionDetail,
    bool DirectUsable,
    string DirectUsableText,
    int MaxConcurrency,
    int SampleCount,
    int TimeoutMs)
{
    public IReadOnlyList<string> DomainsProbed => Domains.Select(d => d.Domain).ToList();
}

/// <summary>进度：已完成 / 共 N 个候选 IP（不是百分比）。</summary>
public sealed record DiagnosticsProgress(int CompletedIpCount, int TotalIpCount);

/// <summary>M1 的固定范围：核心非 CDN 域名 + 国内可达 DoH 端点。</summary>
public static class OfficialSiteDiagnosticsDefaults
{
    /// <summary>
    /// 参与观测的核心域名（按设计文档 §1.1 / §1.2 的非 CDN 项）。
    /// <para>
    /// 不含 <c>gist.github.com</c>：设计文档 §1.3 已把 gist 归为“可选、默认不写”，
    /// M1 只观测会影响“解析层是否瓶颈”判断的核心域名；多测一个可选域名只会增加
    /// 探测量，不改变结论，反而多一个“选错就坏一处”的展示噪声。
    /// </para>
    /// <para>
    /// 不含 <c>github.githubassets.com</c> / <c>avatars.githubusercontent.com</c> 等 CDN 域名：
    /// 固定单个边缘 IP 可能破坏 CDN 调度（设计文档 §8），M1 先不把 CDN 纳入结论。
    /// </para>
    /// </summary>
    public static readonly IReadOnlyList<string> CoreDomains = new[]
    {
        "github.com",
        "api.github.com",
        "codeload.github.com",
        "raw.githubusercontent.com"
    };

    /// <summary>只用国内实测可达的 DoH 源，且不采信任何第三方 hosts 项目数据。</summary>
    public static readonly IReadOnlyList<DohEndpoint> DohEndpoints = new[]
    {
        new DohEndpoint("阿里 DoH", "https://dns.alidns.com/resolve?name={domain}&type=A"),
        new DohEndpoint("腾讯 DoH", "https://doh.pub/dns-query?name={domain}&type=A")
    };

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    public const int SampleCount = 3;
    public const int MaxConcurrency = 4;
}

/// <summary>
/// DoH 的 JSON 响应解析。形状参考 RFC 8484 的 JSON 表示：
/// { "Status": 0, "Answer": [ { "name": "...", "type": 1, "TTL": 30, "data": "1.2.3.4" } ] }
/// 只取 type == 1（A 记录）且可解析为 IPv4 的 data；CNAME / AAAA 一律忽略。
/// </summary>
public static class DohJsonParser
{
    public static DohParseResult ParseAddresses(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new DohParseResult(Array.Empty<string>(), "响应为空");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return new DohParseResult(Array.Empty<string>(), "响应不是合法 JSON");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new DohParseResult(Array.Empty<string>(), "响应不是 JSON 对象");
            }

            if (TryGetProperty(root, "Status", out var status)
                && status.ValueKind == JsonValueKind.Number
                && status.TryGetInt32(out var statusCode)
                && statusCode != 0)
            {
                return new DohParseResult(Array.Empty<string>(), $"DoH 返回状态码 {statusCode}");
            }

            if (!TryGetProperty(root, "Answer", out var answer) || answer.ValueKind != JsonValueKind.Array)
            {
                return new DohParseResult(Array.Empty<string>(), "响应未包含 Answer 记录");
            }

            var addresses = new List<string>();
            foreach (var entry in answer.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object) continue;
                if (!TryGetProperty(entry, "type", out var type)
                    || type.ValueKind != JsonValueKind.Number
                    || !type.TryGetInt32(out var typeCode)
                    || typeCode != 1)
                {
                    continue; // CNAME(5) / AAAA(28) 等非 A 记录
                }

                if (!TryGetProperty(entry, "data", out var data) || data.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var text = data.GetString();
                if (!IPAddress.TryParse(text, out var address)) continue;
                if (address.AddressFamily != AddressFamily.InterNetwork) continue;

                var normalized = address.ToString();
                if (!addresses.Contains(normalized, StringComparer.Ordinal))
                {
                    addresses.Add(normalized);
                }
            }

            return addresses.Count == 0
                ? new DohParseResult(addresses, "响应未返回 IPv4 A 记录")
                : new DohParseResult(addresses, null);
        }
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}

/// <summary>默认 DoH 客户端：5 秒超时，Accept: application/dns-json。</summary>
public sealed class HttpDohClient : IDohClient, IDisposable
{
    private readonly HttpClient _http;
    private readonly TimeSpan _timeout;

    public HttpDohClient(TimeSpan? timeout = null)
    {
        _timeout = timeout ?? OfficialSiteDiagnosticsDefaults.Timeout;
        _http = new HttpClient { Timeout = _timeout };
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/dns-json"));
    }

    public async Task<DohQueryOutcome> QueryAsync(DohEndpoint endpoint, string domain, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var url = endpoint.UrlTemplate.Replace("{domain}", Uri.EscapeDataString(domain), StringComparison.Ordinal);
            using var response = await _http.GetAsync(url, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                return new DohQueryOutcome(domain, endpoint.Name, false, Array.Empty<string>(),
                    stopwatch.ElapsedMilliseconds, $"HTTP {(int)response.StatusCode}");
            }

            var parsed = DohJsonParser.ParseAddresses(body);
            return new DohQueryOutcome(domain, endpoint.Name, parsed.Error == null, parsed.Addresses,
                stopwatch.ElapsedMilliseconds, parsed.Error);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // 用户取消，不属于“失败原因”
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            return new DohQueryOutcome(domain, endpoint.Name, false, Array.Empty<string>(),
                stopwatch.ElapsedMilliseconds, $"请求超时（{_timeout.TotalSeconds:0}s）");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new DohQueryOutcome(domain, endpoint.Name, false, Array.Empty<string>(),
                stopwatch.ElapsedMilliseconds, $"网络错误：{NormalizeMessage(ex)}");
        }
    }

    public void Dispose() => _http.Dispose();

    private static string NormalizeMessage(Exception ex)
    {
        var message = (ex.InnerException ?? ex).Message;
        message = message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return message.Length > 160 ? message[..160] : message;
    }
}

/// <summary>默认探测：TCP 443 建连 + 带正确 SNI 的 TLS 握手，证书按系统默认校验。</summary>
public sealed class TcpTlsHandshakeProbe : IHandshakeProbe
{
    public async Task<HandshakeSample> MeasureAsync(
        string domain, string ip, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var stage = "TCP 443 连接";

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);

        try
        {
            using var tcp = new TcpClient { NoDelay = true };
            await tcp.ConnectAsync(ip, 443, linked.Token);

            stage = "TLS 握手";
            await using var ssl = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false);
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = domain,
                EnabledSslProtocols = SslProtocols.None // 交给系统协商，不强制旧协议
            }, linked.Token);

            stopwatch.Stop();
            return new HandshakeSample((int)stopwatch.ElapsedMilliseconds, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new HandshakeSample(null, $"{stage}超时（{timeout.TotalSeconds:0}s）");
        }
        catch (Exception ex)
        {
            return new HandshakeSample(null, $"{stage}失败：{NormalizeMessage(ex)}");
        }
    }

    private static string NormalizeMessage(Exception ex)
    {
        var message = (ex.InnerException ?? ex).Message;
        message = message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return message.Length > 160 ? message[..160] : message;
    }
}

/// <summary>系统 DNS 解析（只读）：只取 IPv4，结果就是“当前系统解析”的基线来源。</summary>
public sealed class SystemDnsResolver : ISystemDnsResolver
{
    public async Task<SystemResolution> ResolveAsync(string domain, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(domain, cancellationToken);
            stopwatch.Stop();
            var ipv4 = addresses
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                .Select(a => a.ToString())
                .Distinct(StringComparer.Ordinal)
                .ToList();

            return ipv4.Count == 0
                ? new SystemResolution(ipv4, stopwatch.ElapsedMilliseconds, "系统 DNS 未解析到 IPv4 地址")
                : new SystemResolution(ipv4, stopwatch.ElapsedMilliseconds, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new SystemResolution(Array.Empty<string>(), stopwatch.ElapsedMilliseconds,
                $"系统 DNS 解析失败：{ex.Message}");
        }
    }
}

/// <summary>
/// M1 只读诊断：DoH 取候选 IP → 每个候选做 TCP+TLS 多次采样取中位数 →
/// 与系统当前解析（OS DNS）的 IP 并排对比 → 给出“解析层是否瓶颈”的结论。
/// <para>
/// 只读保证：本类只依赖 <see cref="IDohClient"/>、<see cref="IHandshakeProbe"/>、
/// <see cref="ISystemDnsResolver"/> 三个只读接口，不引用任何写 Hosts / 写系统配置的类型
/// （由测试 <c>OfficialSiteDiagnosticsTests</c> 断言）。
/// </para>
/// </summary>
public sealed class OfficialSiteDiagnosticsService
{
    private readonly IDohClient _dohClient;
    private readonly IHandshakeProbe _probe;
    private readonly ISystemDnsResolver _dnsResolver;
    private readonly IReadOnlyList<DohEndpoint> _endpoints;
    private readonly IReadOnlyList<string> _domains;
    private readonly TimeSpan _timeout;
    private readonly int _sampleCount;
    private readonly int _maxConcurrency;
    private readonly TimeSpan _cacheTtl;
    private readonly Func<DateTimeOffset> _clock;

    private OfficialSiteDiagnosticsReport? _cached;
    private DateTimeOffset _cachedAt;

    public OfficialSiteDiagnosticsService(
        IDohClient? dohClient = null,
        IHandshakeProbe? probe = null,
        ISystemDnsResolver? dnsResolver = null,
        IReadOnlyList<DohEndpoint>? endpoints = null,
        IReadOnlyList<string>? domains = null,
        TimeSpan? timeout = null,
        int sampleCount = OfficialSiteDiagnosticsDefaults.SampleCount,
        int maxConcurrency = OfficialSiteDiagnosticsDefaults.MaxConcurrency,
        TimeSpan? cacheTtl = null,
        Func<DateTimeOffset>? clock = null)
    {
        _dohClient = dohClient ?? new HttpDohClient(timeout);
        _probe = probe ?? new TcpTlsHandshakeProbe();
        _dnsResolver = dnsResolver ?? new SystemDnsResolver();
        _endpoints = endpoints ?? OfficialSiteDiagnosticsDefaults.DohEndpoints;
        _domains = domains ?? OfficialSiteDiagnosticsDefaults.CoreDomains;
        _timeout = timeout ?? OfficialSiteDiagnosticsDefaults.Timeout;
        _sampleCount = Math.Max(1, sampleCount);
        _maxConcurrency = Math.Max(1, maxConcurrency);
        // 设计文档 §6：结果缓存 24 小时；手动“重新检测”传入 forceRefresh 绕过缓存。
        _cacheTtl = cacheTtl ?? TimeSpan.FromHours(24);
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    public async Task<OfficialSiteDiagnosticsReport> RunAsync(
        bool forceRefresh = false,
        IProgress<DiagnosticsProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!forceRefresh && _cached != null && _clock() - _cachedAt < _cacheTtl)
        {
            return _cached;
        }

        var stopwatch = Stopwatch.StartNew();
        using var gate = new SemaphoreSlim(_maxConcurrency, _maxConcurrency);

        // 1) DoH 查询 + 系统 DNS 解析（都只读，并发受同一上限约束）。
        var dohTasks = _domains
            .SelectMany(domain => _endpoints.Select(endpoint => QueryDohAsync(gate, endpoint, domain, cancellationToken)))
            .ToArray();
        var dnsTasks = _domains
            .Select(async domain => (Domain: domain, Resolution: await _dnsResolver.ResolveAsync(domain, cancellationToken)))
            .ToArray();

        var dohOutcomes = await Task.WhenAll(dohTasks).ConfigureAwait(false);
        var dnsResolutions = (await Task.WhenAll(dnsTasks).ConfigureAwait(false))
            .ToDictionary(x => x.Domain, x => x.Resolution, StringComparer.Ordinal);
        var resolutionElapsed = stopwatch.ElapsedMilliseconds;

        // 2) 汇总每个域名的 IP：DoH 候选（保留首个来源） + 系统解析 IP（基线）。
        var plan = new List<DomainPlan>();
        foreach (var domain in _domains)
        {
            var outcomes = dohOutcomes.Where(o => o.Domain == domain).ToList();
            var dns = dnsResolutions[domain];
            var sources = new Dictionary<string, string>(StringComparer.Ordinal);
            var dohIps = new List<string>();
            foreach (var outcome in outcomes.Where(o => o.Success))
            {
                foreach (var ip in outcome.Addresses)
                {
                    if (!sources.ContainsKey(ip))
                    {
                        sources[ip] = outcome.SourceName;
                        dohIps.Add(ip);
                    }
                }
            }

            // 同一个 IP 可能既来自 DoH 又来自系统解析：测量只做一次，但两侧都保留一行，
            // 让用户能直接看到“DoH 返回的 IP 与系统当前解析一致/不一致”。
            var probeIps = dohIps
                .Concat(dns.Ipv4Addresses)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            plan.Add(new DomainPlan(domain, outcomes, dns, dohIps, probeIps, sources));
        }

        // 3) 同一域名下同一个 IP 只测一次（DoH 候选与基线重合时复用结果）。
        var totalIpCount = plan.Sum(p => p.ProbeIps.Count);
        var completed = 0;
        var measureTasks = new List<Task<(string Domain, string Ip, Measurement Measurement)>>();
        foreach (var domainPlan in plan)
        {
            foreach (var ip in domainPlan.ProbeIps)
            {
                measureTasks.Add(MeasureAsync(gate, domainPlan.Domain, ip, () =>
                {
                    var done = Interlocked.Increment(ref completed);
                    progress?.Report(new DiagnosticsProgress(done, totalIpCount));
                }, cancellationToken));
            }
        }

        var measuredResults = await Task.WhenAll(measureTasks).ConfigureAwait(false);
        var measurements = new Dictionary<string, Dictionary<string, Measurement>>(StringComparer.Ordinal);
        foreach (var item in measuredResults)
        {
            measurements.TryAdd(item.Domain, new Dictionary<string, Measurement>(StringComparer.Ordinal));
            measurements[item.Domain][item.Ip] = item.Measurement;
        }

        stopwatch.Stop();

        // 4) 生成每个域名的行：候选按实测排序，基线行放在其后（并排对比列）。
        var domains = new List<DomainDiagnostics>();
        foreach (var domainPlan in plan)
        {
            var measured = measurements[domainPlan.Domain];
            var baselineIps = domainPlan.Dns.Ipv4Addresses;
            var baselineMedians = baselineIps
                .Select(ip => measured.TryGetValue(ip, out var m) && m.Available ? m.MedianMs : null)
                .Where(ms => ms.HasValue)
                .Select(ms => ms!.Value)
                .ToList();
            var baselineMedian = baselineMedians.Count > 0 ? baselineMedians.Min() : (int?)null;

            var rows = new List<IpDiagnosticsRow>();
            foreach (var ip in domainPlan.DohIps)
            {
                rows.Add(BuildRow(domainPlan.Domain, ip, domainPlan.Sources[ip], false, measured[ip], baselineMedian));
            }

            foreach (var ip in baselineIps)
            {
                measured.TryGetValue(ip, out var measurement);
                rows.Add(measurement == null
                    ? new IpDiagnosticsRow(domainPlan.Domain, ip, "系统 DNS（基线）", true, null, 0, _sampleCount,
                        false, domainPlan.Dns.FailureReason ?? "未测量", BaselineComparison.None)
                    : BuildRow(domainPlan.Domain, ip, "系统 DNS（基线）", true, measurement, null));
            }

            rows = rows
                .OrderBy(r => r.IsBaseline ? 1 : 0)
                .ThenBy(r => r.Available ? 0 : 1)
                .ThenBy(r => r.MedianMs ?? int.MaxValue)
                .ThenBy(r => r.Ip, StringComparer.Ordinal)
                .ToList();

            domains.Add(new DomainDiagnostics(domainPlan.Domain, domainPlan.Doh, rows,
                baselineMedian, domainPlan.Dns.FailureReason));
        }

        // 5) 结论：这是本面板的主要产出，不是数字堆砌。
        var (kind, title, detail) = BuildConclusion(domains);
        var githubBaseline = domains.FirstOrDefault(d => d.Domain == "github.com")?.Rows
            .FirstOrDefault(r => r.IsBaseline);
        var directUsable = githubBaseline?.Available == true;
        var directText = directUsable
            ? $"直连可用（github.com TLS 握手 {githubBaseline!.MedianMs}ms；仅证明握手成功，不等于页面完整加载）"
            : $"直连不可用（{(githubBaseline?.FailureReason ?? "github.com 基线未测到")}）";

        var report = new OfficialSiteDiagnosticsReport(
            _clock(),
            stopwatch.ElapsedMilliseconds,
            resolutionElapsed,
            domains,
            kind,
            title,
            detail,
            directUsable,
            directText,
            _maxConcurrency,
            _sampleCount,
            (int)_timeout.TotalMilliseconds);

        _cached = report;
        _cachedAt = report.CheckedAt;
        return report;
    }

    private async Task<DohQueryOutcome> QueryDohAsync(
        SemaphoreSlim gate, DohEndpoint endpoint, string domain, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await _dohClient.QueryAsync(endpoint, domain, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<(string Domain, string Ip, Measurement Measurement)> MeasureAsync(
        SemaphoreSlim gate, string domain, string ip, Action onCompleted, CancellationToken cancellationToken)
    {
        var samples = new List<HandshakeSample>(_sampleCount);
        for (var i = 0; i < _sampleCount; i++)
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                samples.Add(await _probe.MeasureAsync(domain, ip, _timeout, cancellationToken).ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                samples.Add(new HandshakeSample(null, $"探测异常：{ex.Message}"));
            }
            finally
            {
                gate.Release();
            }
        }

        onCompleted();
        return (domain, ip, new Measurement(samples));
    }

    private static IpDiagnosticsRow BuildRow(
        string domain, string ip, string source, bool isBaseline, Measurement measurement, int? baselineMedian)
    {
        return new IpDiagnosticsRow(
            domain,
            ip,
            source,
            isBaseline,
            measurement.MedianMs,
            measurement.SuccessSamples,
            measurement.SampleCount,
            measurement.Available,
            measurement.FailureReason,
            isBaseline ? BaselineComparison.None : Compare(measurement.MedianMs, baselineMedian));
    }

    /// <summary>20% 以上差异才分类为更快/更慢；其余视为相近。无法比较返回 None。</summary>
    public static BaselineComparison Compare(int? candidateMs, int? baselineMs)
    {
        if (candidateMs is null || baselineMs is null || baselineMs <= 0) return BaselineComparison.None;
        if (candidateMs.Value * 10 < baselineMs.Value * 8) return BaselineComparison.Faster;
        if (candidateMs.Value * 10 > baselineMs.Value * 12) return BaselineComparison.Slower;
        return BaselineComparison.Similar;
    }

    /// <summary>中位数：样本排序后取中间；偶数个取中间两个的均值（四舍五入）。</summary>
    public static int? ComputeMedian(IEnumerable<int?> samples)
    {
        var values = samples.Where(s => s.HasValue).Select(s => s!.Value).OrderBy(v => v).ToList();
        if (values.Count == 0) return null;
        var middle = values.Count / 2;
        if (values.Count % 2 == 1) return values[middle];
        return (int)Math.Round((values[middle - 1] + values[middle]) / 2.0, MidpointRounding.AwayFromZero);
    }

    private static (DiagnosticsConclusionKind Kind, string Title, string Detail) BuildConclusion(
        IReadOnlyList<DomainDiagnostics> domains)
    {
        var faster = domains
            .SelectMany(d => d.Candidates
                .Where(c => c.Available && c.Comparison == BaselineComparison.Faster)
                .Select(c => (Domain: d.Domain, Row: c, Baseline: d.BaselineMedianMs)))
            .OrderBy(x => x.Row.MedianMs)
            .ToList();

        var observation = BuildAdditionalObservation(domains);

        if (faster.Count > 0)
        {
            var best = faster[0];
            var baseline = best.Baseline ?? 0;
            var gain = baseline > 0 ? (baseline - best.Row.MedianMs!.Value) * 100.0 / baseline : 0;
            var detail =
                $"最快候选 {best.Domain} / {best.Row.Ip}：{best.Row.MedianMs}ms，系统解析基线 {baseline}ms" +
                $"（快 {gain:0}%）。本次仅观测，未写入 Hosts；是否启用 Hosts 优选需另行决定。" + observation;
            return (DiagnosticsConclusionKind.OptimizationPossible,
                "有优化空间，可考虑启用 Hosts 优选", detail);
        }

        var measuredCandidates = domains
            .SelectMany(d => d.Candidates
                .Where(c => c.Available)
                .Select(c => (Domain: d.Domain, Row: c, Baseline: d.BaselineMedianMs)))
            .OrderBy(x => x.Row.MedianMs)
            .ToList();

        if (measuredCandidates.Count > 0)
        {
            var best = measuredCandidates[0];
            var detail = best.Baseline is int baseline
                ? $"本次最快候选 {best.Domain} / {best.Row.Ip}：{best.Row.MedianMs}ms，" +
                  $"系统解析基线 {baseline}ms，差距未到 20%（换 IP 不会更快）。"
                : $"本次最快候选 {best.Domain} / {best.Row.Ip}：{best.Row.MedianMs}ms，" +
                  "但系统解析基线不可用，无法比较。";
            return (DiagnosticsConclusionKind.NoExpectedGain,
                "当前网络换 IP 改善不了（瓶颈不在解析层）", detail + observation);
        }

        var failures = domains
            .SelectMany(d => d.DohResults.Where(r => !r.Success).Select(r => $"{d.Domain} / {r.SourceName}：{r.FailureReason}"))
            .Concat(domains.SelectMany(d => d.Rows
                .Where(r => !r.Available && r.FailureReason != null)
                .Select(r => $"{d.Domain} / {r.Ip}：{r.FailureReason}")))
            .Take(3)
            .ToList();
        var baselineFailures = domains
            .Where(d => d.BaselineFailureReason != null)
            .Select(d => $"{d.Domain}：{d.BaselineFailureReason}")
            .Take(2)
            .ToList();
        var reasons = failures.Concat(baselineFailures).ToList();
        var reasonText = reasons.Count > 0 ? string.Join("；", reasons) : "未取得可用候选 IP";
        return (DiagnosticsConclusionKind.Inconclusive,
            "无法判断：本次未取得可用候选 IP",
            $"{reasonText}。这不代表网络一定有问题，可稍后点“重新检测”复测。");
    }

    /// <summary>
    /// 补充观察：某个域名的系统解析 IP 完全不可用、而 DoH 候选可握手时，单独说明。
    /// 这类差异常见于 CDN 边缘（设计文档 §8），随时段变化，不据此单独翻转整体结论。
    /// </summary>
    private static string BuildAdditionalObservation(IReadOnlyList<DomainDiagnostics> domains)
    {
        var observations = domains
            .Where(d => d.BaselineMedianMs is null && d.Candidates.Any(c => c.Available))
            .Select(d =>
            {
                var usable = d.Candidates.Where(c => c.Available).OrderBy(c => c.MedianMs).ToList();
                var reason = d.Rows.FirstOrDefault(r => r.IsBaseline)?.FailureReason
                             ?? d.BaselineFailureReason
                             ?? "基线不可用";
                return $"{d.Domain}：系统解析基线不可用（{reason}），DoH 候选 {usable.Count} 个可握手（最快 {usable[0].MedianMs}ms）";
            })
            .ToList();

        return observations.Count == 0
            ? string.Empty
            : " 另：" + string.Join("；", observations) + "。该差异可能随时段/CDN 边缘变化，未据此判定整体收益。";
    }

    private sealed record DomainPlan(
        string Domain,
        IReadOnlyList<DohQueryOutcome> Doh,
        SystemResolution Dns,
        IReadOnlyList<string> DohIps,
        IReadOnlyList<string> ProbeIps,
        IReadOnlyDictionary<string, string> Sources);

    private sealed class Measurement
    {
        public Measurement(IReadOnlyList<HandshakeSample> samples)
        {
            Samples = samples;
        }

        public IReadOnlyList<HandshakeSample> Samples { get; }

        public int SampleCount => Samples.Count;

        public int SuccessSamples => Samples.Count(s => s.Success);

        public int? MedianMs => ComputeMedian(Samples.Select(s => s.ElapsedMs));

        /// <summary>多数采样成功才算可用：3 次里至少 2 次成功，避免一次偶然成功被标成“可用”。</summary>
        public bool Available => SuccessSamples >= SampleCount / 2 + 1;

        public string? FailureReason
        {
            get
            {
                if (Available) return null;
                var reasons = Samples
                    .Where(s => !s.Success)
                    .Select(s => s.FailureReason)
                    .Where(r => !string.IsNullOrWhiteSpace(r))
                    .Distinct()
                    .ToList();
                var first = reasons.Count > 0 ? reasons[0] : "探测未返回结果";
                return SuccessSamples > 0
                    ? $"{SampleCount} 次采样仅 {SuccessSamples} 次成功：{first}"
                    : first;
            }
        }
    }
}
