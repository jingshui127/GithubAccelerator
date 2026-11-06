using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using GithubAccelerator.Core.Services;
using Xunit;

namespace GithubAccelerator.Tests;

public class OfficialSiteDiagnosticsTests
{
    // ---------------------------------------------------------------- fakes

    private sealed class FakeDohClient : IDohClient
    {
        private readonly Dictionary<string, DohQueryOutcome> _outcomes;

        public FakeDohClient(Dictionary<string, DohQueryOutcome> outcomes)
        {
            _outcomes = outcomes;
        }

        public int Calls { get; private set; }

        public Task<DohQueryOutcome> QueryAsync(DohEndpoint endpoint, string domain, CancellationToken cancellationToken)
        {
            Calls++;
            var key = domain + "|" + endpoint.Name;
            if (_outcomes.TryGetValue(key, out var outcome))
            {
                return Task.FromResult(outcome with { Domain = domain, SourceName = endpoint.Name });
            }

            return Task.FromResult(new DohQueryOutcome(domain, endpoint.Name, false, Array.Empty<string>(),
                5000, "请求超时（5s）"));
        }
    }

    private sealed class FakeHandshakeProbe : IHandshakeProbe
    {
        private readonly Dictionary<string, ConcurrentQueue<HandshakeSample>> _samples;
        private readonly int _delayMs;
        private int _current;
        private int _maxConcurrent;
        private int _calls;

        public FakeHandshakeProbe(Dictionary<string, ConcurrentQueue<HandshakeSample>> samples, int delayMs = 0)
        {
            _samples = samples;
            _delayMs = delayMs;
        }

        public int Calls => Volatile.Read(ref _calls);

        public int MaxConcurrent => Volatile.Read(ref _maxConcurrent);

        public async Task<HandshakeSample> MeasureAsync(
            string domain, string ip, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            var now = Interlocked.Increment(ref _current);
            UpdateMax(now);
            try
            {
                if (_delayMs > 0)
                {
                    await Task.Delay(_delayMs, cancellationToken);
                }

                var key = domain + "|" + ip;
                if (!_samples.TryGetValue(key, out var queue) || !queue.TryDequeue(out var sample))
                {
                    return new HandshakeSample(100, null);
                }

                return sample;
            }
            finally
            {
                Interlocked.Decrement(ref _current);
            }
        }

        private void UpdateMax(int value)
        {
            while (true)
            {
                var current = Volatile.Read(ref _maxConcurrent);
                if (value <= current) return;
                if (Interlocked.CompareExchange(ref _maxConcurrent, value, current) == current) return;
            }
        }
    }

    private sealed class FakeDnsResolver : ISystemDnsResolver
    {
        private readonly Dictionary<string, SystemResolution> _resolutions;

        public FakeDnsResolver(Dictionary<string, SystemResolution> resolutions)
        {
            _resolutions = resolutions;
        }

        public Task<SystemResolution> ResolveAsync(string domain, CancellationToken cancellationToken)
        {
            return Task.FromResult(_resolutions.TryGetValue(domain, out var resolution)
                ? resolution
                : new SystemResolution(Array.Empty<string>(), 1, "系统 DNS 未解析到 IPv4 地址"));
        }
    }

    // ---------------------------------------------------------------- builders

    private const string AliDoh = "阿里 DoH";
    private const string TencentDoh = "腾讯 DoH";
    private const string Domain = "github.com";
    private const string CandidateIp = "20.205.243.166";
    private const string BaselineIp = "140.82.114.4";

    private static readonly IReadOnlyList<DohEndpoint> OneDoh =
        new[] { new DohEndpoint(AliDoh, "https://dns.alidns.com/resolve?name={domain}&type=A") };

    private static readonly IReadOnlyList<string> OneDomain = new[] { Domain };

    private static OfficialSiteDiagnosticsService CreateService(
        IDohClient doh,
        IHandshakeProbe probe,
        ISystemDnsResolver dns,
        IReadOnlyList<DohEndpoint>? endpoints = null,
        IReadOnlyList<string>? domains = null,
        int sampleCount = 3,
        int maxConcurrency = 4,
        TimeSpan? timeout = null,
        TimeSpan? cacheTtl = null,
        Func<DateTimeOffset>? clock = null)
    {
        return new OfficialSiteDiagnosticsService(doh, probe, dns,
            endpoints ?? OneDoh,
            domains ?? OneDomain,
            timeout ?? TimeSpan.FromSeconds(5),
            sampleCount,
            maxConcurrency,
            cacheTtl ?? TimeSpan.FromHours(24),
            clock ?? new Func<DateTimeOffset>(() => new DateTimeOffset(2026, 11, 6, 15, 0, 0, TimeSpan.FromHours(8))));
    }

    private static ConcurrentQueue<HandshakeSample> Queue(params HandshakeSample[] samples)
        => new(samples);

    private static HandshakeSample Ok(int ms) => new(ms, null);

    private static HandshakeSample Fail(string reason) => new(null, reason);

    private static Dictionary<string, ConcurrentQueue<HandshakeSample>> Samples(
        string domain, string ip, params HandshakeSample[] samples)
    {
        return new Dictionary<string, ConcurrentQueue<HandshakeSample>>
        {
            [domain + "|" + ip] = Queue(samples)
        };
    }

    private static Dictionary<string, SystemResolution> Dns(string domain, params string[] ips)
        => new() { [domain] = new SystemResolution(ips.ToList(), 1, ips.Length == 0 ? "系统 DNS 未解析到 IPv4 地址" : null) };

    private static Dictionary<string, DohQueryOutcome> DohOk(string domain, string source, params string[] ips)
        => new()
        {
            [domain + "|" + source] = new DohQueryOutcome(domain, source, true, ips.ToList(), 200, null)
        };

    // ---------------------------------------------------------------- DoH 解析

    [Fact]
    public void ParseAddresses_ReturnsOnlyIpv4ARecords_AndDeduplicates()
    {
        const string json = """
        {
          "Status": 0,
          "Answer": [
            {"name":"github.com.","type":1,"TTL":53,"data":"20.205.243.166"},
            {"name":"github.com.","type":1,"TTL":53,"data":"20.205.243.166"},
            {"name":"github.com.","type":5,"TTL":53,"data":"github.com."},
            {"name":"github.com.","type":28,"TTL":53,"data":"2606:50c0:8000::154"},
            {"name":"github.com.","type":1,"TTL":53,"data":"140.82.114.4"}
          ]
        }
        """;

        var result = DohJsonParser.ParseAddresses(json);

        Assert.Null(result.Error);
        Assert.Equal(new[] { "20.205.243.166", "140.82.114.4" }, result.Addresses);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"Status\":3}")]
    [InlineData("{\"Status\":0,\"Answer\":[]}")]
    [InlineData("{\"Status\":0,\"Answer\":[{\"type\":1,\"data\":\"not-an-ip\"}]}")]
    public void ParseAddresses_HandlesEmptyAndMalformedResponses_WithoutThrowing(string json)
    {
        var result = DohJsonParser.ParseAddresses(json);

        Assert.Empty(result.Addresses);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void ParseAddresses_ReadsStatusAndAnswerCaseInsensitively()
    {
        const string json = "{\"status\":0,\"answer\":[{\"TYPE\":1,\"data\":\"1.2.3.4\"}]}";

        var result = DohJsonParser.ParseAddresses(json);

        Assert.Null(result.Error);
        Assert.Equal(new[] { "1.2.3.4" }, result.Addresses);
    }

    // ---------------------------------------------------------------- 中位数与分类

    [Fact]
    public void ComputeMedian_TakesMiddleValue_AndIgnoresFailures()
    {
        Assert.Equal(400, OfficialSiteDiagnosticsService.ComputeMedian(new int?[] { 500, 300, 400 }));
        Assert.Equal(300, OfficialSiteDiagnosticsService.ComputeMedian(new int?[] { null, 300, null }));
        Assert.Equal(400, OfficialSiteDiagnosticsService.ComputeMedian(new int?[] { 300, 500 }));
        Assert.Null(OfficialSiteDiagnosticsService.ComputeMedian(new int?[] { null, null }));
    }

    [Theory]
    [InlineData(399, 500, BaselineComparison.Faster)]
    [InlineData(400, 500, BaselineComparison.Similar)]   // 恰好 20%，不算“更快”
    [InlineData(500, 500, BaselineComparison.Similar)]
    [InlineData(600, 500, BaselineComparison.Similar)]   // 恰好 20%，不算“更慢”
    [InlineData(601, 500, BaselineComparison.Slower)]
    [InlineData(null, 500, BaselineComparison.None)]
    [InlineData(400, null, BaselineComparison.None)]
    public void Compare_UsesTwentyPercentThreshold(int? candidate, int? baseline, BaselineComparison expected)
    {
        Assert.Equal(expected, OfficialSiteDiagnosticsService.Compare(candidate, baseline));
    }

    // ---------------------------------------------------------------- 端到端（假网络）

    [Fact]
    public async Task RunAsync_ComparesCandidateWithBaseline_AndConcludesOptimizationPossible()
    {
        var probe = new FakeHandshakeProbe(new Dictionary<string, ConcurrentQueue<HandshakeSample>>
        {
            [Domain + "|" + CandidateIp] = Queue(Ok(200), Ok(220), Ok(200)),
            [Domain + "|" + BaselineIp] = Queue(Ok(400), Ok(420), Ok(400))
        });
        var service = CreateService(new FakeDohClient(DohOk(Domain, AliDoh, CandidateIp)),
            probe, new FakeDnsResolver(Dns(Domain, BaselineIp)));

        var report = await service.RunAsync();

        Assert.Equal(DiagnosticsConclusionKind.OptimizationPossible, report.ConclusionKind);
        Assert.Contains("有优化空间", report.ConclusionTitle);
        Assert.Contains("未写入 Hosts", report.ConclusionDetail);
        Assert.True(report.DirectUsable);
        Assert.Contains("直连可用", report.DirectUsableText);

        var domain = report.Domains.Single();
        Assert.Equal(2, domain.Rows.Count);
        var candidate = domain.Rows.Single(r => !r.IsBaseline);
        var baseline = domain.Rows.Single(r => r.IsBaseline);
        Assert.Equal(200, candidate.MedianMs);
        Assert.Equal(BaselineComparison.Faster, candidate.Comparison);
        Assert.Equal("更快", candidate.ComparisonText);
        Assert.Equal(400, baseline.MedianMs);
        Assert.Equal(400, domain.BaselineMedianMs);
        Assert.Single(domain.Candidates);
    }

    [Fact]
    public async Task RunAsync_WhenCandidateIsNotFaster_ConcludesNoGain()
    {
        var probe = new FakeHandshakeProbe(new Dictionary<string, ConcurrentQueue<HandshakeSample>>
        {
            [Domain + "|" + CandidateIp] = Queue(Ok(500), Ok(500), Ok(500)),
            [Domain + "|" + BaselineIp] = Queue(Ok(400), Ok(400), Ok(400))
        });
        var service = CreateService(new FakeDohClient(DohOk(Domain, AliDoh, CandidateIp)),
            probe, new FakeDnsResolver(Dns(Domain, BaselineIp)));

        var report = await service.RunAsync();

        Assert.Equal(DiagnosticsConclusionKind.NoExpectedGain, report.ConclusionKind);
        Assert.Contains("瓶颈不在解析层", report.ConclusionTitle);
        Assert.Contains("Hosts 加速预计无收益", report.ConclusionTitle);
        Assert.Contains("500ms", report.ConclusionDetail);
        Assert.Contains("400ms", report.ConclusionDetail);
    }

    [Fact]
    public async Task RunAsync_WhenAllSamplesFail_ShowsReasonAndNeverMarksAvailable()
    {
        var probe = new FakeHandshakeProbe(new Dictionary<string, ConcurrentQueue<HandshakeSample>>
        {
            [Domain + "|" + CandidateIp] = Queue(Fail("TCP 443 连接超时（5s）"), Fail("TCP 443 连接超时（5s）"), Fail("TCP 443 连接超时（5s）")),
            [Domain + "|" + BaselineIp] = Queue(Fail("TLS 握手失败：证书不匹配"), Fail("TLS 握手失败：证书不匹配"), Fail("TLS 握手失败：证书不匹配"))
        });
        var service = CreateService(new FakeDohClient(DohOk(Domain, AliDoh, CandidateIp)),
            probe, new FakeDnsResolver(Dns(Domain, BaselineIp)));

        var report = await service.RunAsync();

        var candidate = report.Domains.Single().Rows.Single(r => !r.IsBaseline);
        Assert.False(candidate.Available);
        Assert.Null(candidate.MedianMs);
        Assert.Contains("TCP 443 连接超时（5s）", candidate.AvailabilityText);
        Assert.False(report.DirectUsable);
        Assert.Contains("直连不可用", report.DirectUsableText);
        Assert.Equal(DiagnosticsConclusionKind.Inconclusive, report.ConclusionKind);
        Assert.Contains("TCP 443 连接超时", report.ConclusionDetail);
    }

    [Fact]
    public async Task RunAsync_RequiresMajorityOfSamplesToMarkAvailable()
    {
        var probe = new FakeHandshakeProbe(new Dictionary<string, ConcurrentQueue<HandshakeSample>>
        {
            [Domain + "|" + CandidateIp] = Queue(Ok(300), Fail("TCP 443 连接超时（5s）"), Fail("TCP 443 连接超时（5s）")),
            [Domain + "|" + BaselineIp] = Queue(Ok(400), Ok(400), Ok(400))
        });
        var service = CreateService(new FakeDohClient(DohOk(Domain, AliDoh, CandidateIp)),
            probe, new FakeDnsResolver(Dns(Domain, BaselineIp)));

        var report = await service.RunAsync();

        var candidate = report.Domains.Single().Rows.Single(r => !r.IsBaseline);
        Assert.False(candidate.Available); // 3 次中 1 次成功，不足以判定可用
        Assert.Contains("3 次采样仅 1 次成功", candidate.AvailabilityText);
        Assert.Contains("TCP 443 连接超时", candidate.AvailabilityText);
    }

    [Fact]
    public async Task RunAsync_WhenDohFailsButBaselineWorks_ReportsPerSourceReasonAndIsInconclusive()
    {
        var endpoints = new[]
        {
            new DohEndpoint(AliDoh, "https://dns.alidns.com/resolve?name={domain}&type=A"),
            new DohEndpoint(TencentDoh, "https://doh.pub/dns-query?name={domain}&type=A")
        };
        var doh = new FakeDohClient(new Dictionary<string, DohQueryOutcome>
        {
            [Domain + "|" + AliDoh] = new(Domain, AliDoh, false, Array.Empty<string>(), 5000, "请求超时（5s）"),
            [Domain + "|" + TencentDoh] = new(Domain, TencentDoh, false, Array.Empty<string>(), 10, "HTTP 403")
        });
        var probe = new FakeHandshakeProbe(Samples(Domain, BaselineIp, Ok(400), Ok(400), Ok(400)));
        var service = CreateService(doh, probe, new FakeDnsResolver(Dns(Domain, BaselineIp)), endpoints);

        var report = await service.RunAsync();

        var domain = report.Domains.Single();
        Assert.Equal(2, domain.DohResults.Count);
        Assert.Contains(domain.DohResults, r => r.SourceName == AliDoh && r.FailureReason!.Contains("请求超时"));
        Assert.Contains(domain.DohResults, r => r.SourceName == TencentDoh && r.FailureReason!.Contains("HTTP 403"));
        Assert.Equal(DiagnosticsConclusionKind.Inconclusive, report.ConclusionKind);
        Assert.Contains("阿里 DoH", report.ConclusionDetail);
        Assert.Contains("请求超时", report.ConclusionDetail);
        Assert.True(report.DirectUsable); // 基线可用，但无候选可比较
    }

    [Fact]
    public async Task RunAsync_WhenBaselineUnavailableButCandidateWorks_AddsObservationNote()
    {
        var probe = new FakeHandshakeProbe(new Dictionary<string, ConcurrentQueue<HandshakeSample>>
        {
            [Domain + "|" + CandidateIp] = Queue(Ok(300), Ok(300), Ok(300)),
            [Domain + "|" + BaselineIp] = Queue(Fail("TCP 443 连接超时（5s）"), Fail("TCP 443 连接超时（5s）"), Fail("TCP 443 连接超时（5s）"))
        });
        var service = CreateService(new FakeDohClient(DohOk(Domain, AliDoh, CandidateIp)),
            probe, new FakeDnsResolver(Dns(Domain, BaselineIp)));

        var report = await service.RunAsync();

        Assert.Equal(DiagnosticsConclusionKind.NoExpectedGain, report.ConclusionKind);
        Assert.Contains("系统解析基线不可用", report.ConclusionDetail);
        Assert.Contains("DoH 候选 1 个可握手", report.ConclusionDetail);
        Assert.Contains("最快 300ms", report.ConclusionDetail);
        Assert.Contains("CDN 边缘", report.ConclusionDetail);
    }

    [Fact]
    public async Task RunAsync_KeepsCandidateAndBaselineRowsSideBySide_WhenDohIpEqualsDnsIp()
    {
        var probe = new FakeHandshakeProbe(Samples(Domain, CandidateIp, Ok(400), Ok(400), Ok(400)));
        var service = CreateService(new FakeDohClient(DohOk(Domain, AliDoh, CandidateIp)),
            probe, new FakeDnsResolver(Dns(Domain, CandidateIp)));

        var report = await service.RunAsync();

        var domain = report.Domains.Single();
        Assert.Equal(2, domain.Rows.Count); // 同一 IP，候选行 + 基线行都保留
        var candidate = domain.Rows.Single(r => !r.IsBaseline);
        Assert.Equal(AliDoh, candidate.Source);
        Assert.Equal(BaselineComparison.Similar, candidate.Comparison);
        Assert.Equal(CandidateIp, domain.Rows.Single(r => r.IsBaseline).Ip);
        // 同一 IP 只测一次（3 次采样），不重复 6 次
        Assert.Equal(3, probe.Calls);
    }

    [Fact]
    public async Task RunAsync_RespectsConcurrencyLimit()
    {
        var domains = new[] { Domain, "api.github.com" };
        var probe = new FakeHandshakeProbe(new Dictionary<string, ConcurrentQueue<HandshakeSample>>
        {
            [Domain + "|" + CandidateIp] = Queue(Ok(200), Ok(200), Ok(200)),
            [Domain + "|" + BaselineIp] = Queue(Ok(300), Ok(300), Ok(300)),
            ["api.github.com|" + CandidateIp] = Queue(Ok(250), Ok(250), Ok(250)),
            ["api.github.com|" + BaselineIp] = Queue(Ok(350), Ok(350), Ok(350))
        }, delayMs: 25);
        var endpoints = new[] { new DohEndpoint(AliDoh, "https://dns.alidns.com/resolve?name={domain}&type=A") };
        var doh = new FakeDohClient(new Dictionary<string, DohQueryOutcome>
        {
            [Domain + "|" + AliDoh] = new(Domain, AliDoh, true, new[] { CandidateIp }, 100, null),
            ["api.github.com|" + AliDoh] = new("api.github.com", AliDoh, true, new[] { CandidateIp }, 100, null),
        });
        var dns = new FakeDnsResolver(new Dictionary<string, SystemResolution>
        {
            [Domain] = new(new[] { BaselineIp }, 1, null),
            ["api.github.com"] = new(new[] { BaselineIp }, 1, null)
        });

        var progress = new CollectingProgress();
        var service = CreateService(doh, probe, dns, endpoints, domains, maxConcurrency: 3);
        var report = await service.RunAsync(true, progress);

        Assert.True(probe.MaxConcurrent <= 3, $"实际并发 {probe.MaxConcurrent} 超过上限 3");
        Assert.Equal(2, report.Domains.Count);
        Assert.Equal(2, report.Domains.Sum(d => d.Candidates.Count));
        Assert.NotEmpty(progress.Items);
        // 并发任务完成顺序 ≠ Report 追加顺序：不能假设最后一条就是最终计数。
        // 正确的不变量是“存在一条完成数=总数的进度”，且每条的总数与计划一致。
        Assert.All(progress.Items, item => Assert.Equal(4, item.TotalIpCount));
        Assert.Equal(4, progress.Items.Max(item => item.CompletedIpCount));
    }

    private sealed class CollectingProgress : IProgress<DiagnosticsProgress>
    {
        public List<DiagnosticsProgress> Items { get; } = new();

        public void Report(DiagnosticsProgress value)
        {
            lock (Items) Items.Add(value);
        }
    }

    [Fact]
    public async Task RunAsync_CachesReportUntilTtl_AndForceRefreshBypasses()
    {
        var now = new DateTimeOffset(2026, 11, 6, 15, 0, 0, TimeSpan.FromHours(8));
        var doh = new FakeDohClient(DohOk(Domain, AliDoh, CandidateIp));
        var probe = new FakeHandshakeProbe(new Dictionary<string, ConcurrentQueue<HandshakeSample>>
        {
            [Domain + "|" + CandidateIp] = Queue(Ok(200), Ok(200), Ok(200)),
            [Domain + "|" + BaselineIp] = Queue(Ok(300), Ok(300), Ok(300))
        });
        var service = CreateService(doh, probe, new FakeDnsResolver(Dns(Domain, BaselineIp)),
            cacheTtl: TimeSpan.FromHours(24), clock: () => now);

        var first = await service.RunAsync();
        var dohCalls = doh.Calls;
        var probeCalls = probe.Calls;

        var second = await service.RunAsync();
        Assert.Equal(dohCalls, doh.Calls);
        Assert.Equal(probeCalls, probe.Calls);
        Assert.Equal(first.CheckedAt, second.CheckedAt);

        now = now.AddMinutes(5);
        var refreshed = await service.RunAsync(forceRefresh: true);
        Assert.True(doh.Calls > dohCalls);
        Assert.True(probe.Calls > probeCalls);
        Assert.Equal(now, refreshed.CheckedAt);
    }

    [Fact]
    public async Task RunAsync_ReportsProbeSettings_ForDisplay()
    {
        var service = CreateService(new FakeDohClient(DohOk(Domain, AliDoh, CandidateIp)),
            new FakeHandshakeProbe(Samples(Domain, CandidateIp, Ok(200), Ok(200), Ok(200))),
            new FakeDnsResolver(Dns(Domain, BaselineIp)),
            maxConcurrency: 2, timeout: TimeSpan.FromSeconds(3), sampleCount: 3);

        var report = await service.RunAsync();

        Assert.Equal(2, report.MaxConcurrency);
        Assert.Equal(3, report.SampleCount);
        Assert.Equal(3000, report.TimeoutMs);
        Assert.True(report.TotalElapsedMs >= 0);
        Assert.True(report.ResolutionElapsedMs >= 0);
    }

    // ---------------------------------------------------------------- “不写 Hosts”的保证

    [Fact]
    public void DiagnosticsTypes_DependOnlyOnReadOnlyAbstractions()
    {
        var forbidden = new[]
        {
            "HostsFileManager", "WindowsHostsFileService", "LinuxHostsFileService",
            "HostsOptimizerService", "GitUrlRewriteService", "HostsBlockStateStore",
            "HostsBackupService", "ElevationService"
        };

        var types = new[]
        {
            typeof(OfficialSiteDiagnosticsService),
            typeof(HttpDohClient),
            typeof(TcpTlsHandshakeProbe),
            typeof(SystemDnsResolver)
        };

        foreach (var type in types)
        {
            foreach (var constructor in type.GetConstructors())
            {
                foreach (var parameter in constructor.GetParameters())
                {
                    Assert.DoesNotContain(parameter.ParameterType.Name, forbidden);
                }
            }

            foreach (var field in type.GetFields(
                         System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic |
                         System.Reflection.BindingFlags.Public))
            {
                Assert.DoesNotContain(field.FieldType.Name, forbidden);
            }
        }

        // 服务只依赖三个只读接口 + 配置参数，没有任何写入通道。
        var serviceCtor = typeof(OfficialSiteDiagnosticsService).GetConstructors().Single();
        var parameterTypes = serviceCtor.GetParameters().Select(p => p.ParameterType).ToList();
        Assert.Contains(typeof(IDohClient), parameterTypes);
        Assert.Contains(typeof(IHandshakeProbe), parameterTypes);
        Assert.Contains(typeof(ISystemDnsResolver), parameterTypes);
    }

    [Fact]
    public async Task RunAsync_DoesNotModifySystemHostsFile()
    {
        if (!OperatingSystem.IsWindows()) return;

        var hostsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");
        if (!File.Exists(hostsPath)) return;

        var before = Sha256(hostsPath);

        var service = CreateService(new FakeDohClient(DohOk(Domain, AliDoh, CandidateIp)),
            new FakeHandshakeProbe(new Dictionary<string, ConcurrentQueue<HandshakeSample>>
            {
                [Domain + "|" + CandidateIp] = Queue(Ok(200), Ok(200), Ok(200)),
                [Domain + "|" + BaselineIp] = Queue(Ok(300), Ok(300), Ok(300))
            }),
            new FakeDnsResolver(Dns(Domain, BaselineIp)));

        var report = await service.RunAsync(forceRefresh: true);
        Assert.Equal(DiagnosticsConclusionKind.OptimizationPossible, report.ConclusionKind);

        var after = Sha256(hostsPath);
        Assert.Equal(before, after);
    }

    private static string Sha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }
}
