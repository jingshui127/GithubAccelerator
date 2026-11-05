using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GithubAccelerator.Core.Services;
using Xunit;

namespace GithubAccelerator.Tests;

public class MirrorHealthServiceTests
{
    private sealed class FakeProbeClient : IMirrorProbeClient
    {
        private readonly Dictionary<string, MirrorProbeResponse> _responses;
        public int Calls { get; private set; }

        public FakeProbeClient(Dictionary<string, MirrorProbeResponse> responses)
        {
            _responses = responses;
        }

        public Task<MirrorProbeResponse> GetAsync(Uri uri, CancellationToken cancellationToken)
        {
            Calls++;
            var host = uri.Host.ToLowerInvariant();
            if (_responses.TryGetValue(host, out var response))
            {
                return Task.FromResult(response);
            }
            return Task.FromResult(new MirrorProbeResponse(0, string.Empty, 5000, "无法解析该镜像"));
        }
    }

    private static readonly MirrorSite[] TestMirrors =
    {
        new("MirrorA", "https://a.example", "https://a.example/{url}", "A"),
        new("MirrorB", "https://b.example", "https://b.example/{url}", "B"),
        new("MirrorC", "https://c.example", "https://c.example/{url}", "C"),
        new("MirrorD", "https://cdn.jsdelivr.net", null, "D")
    };

    private static MirrorHealthReport BuildReport(params MirrorHealthResult[] results)
    {
        var ordered = MirrorHealthService.SortByHealth(results);
        return new MirrorHealthReport(ordered, ordered.Any(r => r.IsHealthy),
            ordered.Any(r => r.IsHealthy) ? "镜像检测完成：可用" : "所有加速镜像本次均不可用。已保留原始链接", DateTimeOffset.Now);
    }

    [Fact]
    public async Task CheckAsync_SortsHealthyByLatency_AndDemotesInvalidAndUnreachable()
    {
        var reports = new Dictionary<string, MirrorProbeResponse>
        {
            ["cdn.jsdelivr.net"] = new(200, "Hello World!\n", 50, null),
            ["a.example"] = new(200, "Hello World!\n", 120, null),
            ["b.example"] = new(200, "<!DOCTYPE html><html>error</html>", 80, null),
            ["c.example"] = new(0, string.Empty, 5000, "请求超时"),
        };

        var service = new MirrorHealthService(TestMirrors, new FakeProbeClient(reports),
            () => new DateTimeOffset(2026, 11, 5, 10, 0, 0, TimeSpan.FromHours(8)));

        var report = await service.CheckAsync();

        Assert.True(report.AnyHealthy);
        Assert.Equal(new[] { "MirrorD", "MirrorA", "MirrorB", "MirrorC" }, report.Ordered.Select(r => r.Name));
        Assert.False(report.Ordered.Single(r => r.Name == "MirrorB").ContentValid);
        Assert.Contains("目标文件", report.Ordered.Single(r => r.Name == "MirrorB").FailureReason);
        Assert.False(report.Ordered.Single(r => r.Name == "MirrorC").Reachable);
        Assert.Equal(50, report.Ordered[0].LatencyMs);
    }

    [Fact]
    public async Task CheckAsync_AllUnavailable_ReturnsReadableMessageWithOriginalLinkHint()
    {
        var service = new MirrorHealthService(TestMirrors,
            new FakeProbeClient(new Dictionary<string, MirrorProbeResponse>()));

        var report = await service.CheckAsync();

        Assert.False(report.AnyHealthy);
        Assert.Contains("所有加速镜像本次均不可用", report.Message);
        Assert.Contains("原始链接", report.Message);
        Assert.Contains("MirrorA", report.Message);
    }

    [Fact]
    public async Task CheckAsync_CachesWithinTtl_AndRefreshesAfterExpiry()
    {
        var client = new FakeProbeClient(new Dictionary<string, MirrorProbeResponse>
        {
            ["a.example"] = new(200, "Hello World!", 100, null)
        });
        var now = new DateTimeOffset(2026, 11, 5, 10, 0, 0, TimeSpan.FromHours(8));
        var service = new MirrorHealthService(TestMirrors, client, () => now,
            TimeSpan.FromMinutes(10));

        await service.CheckAsync();
        var firstCalls = client.Calls;
        await service.CheckAsync();
        Assert.Equal(firstCalls, client.Calls);

        now = now.AddMinutes(11);
        await service.CheckAsync();
        Assert.True(client.Calls > firstCalls);
    }

    [Theory]
    [InlineData("Hello World!", true)]
    [InlineData("  Hello World!\n", true)]
    [InlineData("<!DOCTYPE html><html>blocked</html>", false)]
    [InlineData("404 not found", false)]
    [InlineData("", false)]
    public void IsExpectedContent_RejectsHtmlAndUnrelatedBodies(string body, bool expected)
    {
        Assert.Equal(expected, MirrorHealthService.IsExpectedContent(body));
    }

    private sealed class FakeHealthService : IMirrorHealthService
    {
        private readonly MirrorHealthReport _report;
        public FakeHealthService(MirrorHealthReport report) => _report = report;

        public Task<MirrorHealthReport> CheckAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_report);
    }

    [Fact]
    public async Task ConvertUrlAsync_ReordersMirrorsByMeasuredHealth()
    {
        var report = BuildReport(
            new MirrorHealthResult("GHProxy", "https://ghproxy.net", true, true, 20,
                DateTimeOffset.Now, null),
            new MirrorHealthResult("ghfast.top", "https://ghfast.top", true, true, 300,
                DateTimeOffset.Now, null),
            new MirrorHealthResult("gh.api.99988866.xyz", "https://gh.api.99988866.xyz", false, false, null,
                DateTimeOffset.Now, "请求超时"));

        var service = new MirrorUrlService(new FakeHealthService(report));
        var result = await service.ConvertUrlAsync(
            "https://raw.githubusercontent.com/octocat/Hello-World/master/README");

        Assert.Equal("GHProxy", result.MirrorUrls[0].SiteName);
        Assert.Equal("gh.api.99988866.xyz", result.MirrorUrls[^1].SiteName);
        Assert.Contains("可用", result.HealthMessage);
    }

    [Fact]
    public async Task ConvertUrlAsync_WhenAllSourcesUnavailable_KeepsReadableHint()
    {
        var report = BuildReport(
            new MirrorHealthResult("ghfast.top", "https://ghfast.top", false, false, null,
                DateTimeOffset.Now, "DNS 解析失败"),
            new MirrorHealthResult("GHProxy", "https://ghproxy.net", false, false, null,
                DateTimeOffset.Now, "请求超时"));

        var service = new MirrorUrlService(new FakeHealthService(report));
        var result = await service.ConvertUrlAsync("https://github.com/cli/cli/releases/download/v2.40.0/gh.zip");

        Assert.Equal(3, result.MirrorUrls.Count);
        Assert.Contains("所有加速镜像本次均不可用", result.HealthMessage);
        Assert.Contains("原始链接", result.HealthMessage);
    }

    [Fact]
    public void AvailableMirrors_RemovesDnsDeadSites_AndRequiresHttps()
    {
        var names = MirrorUrlService.AvailableMirrors.Select(m => m.BaseUrl).ToArray();
        Assert.DoesNotContain(names, n => n.Contains("gitmirror.com"));
        Assert.DoesNotContain(MirrorUrlService.GitCloneMirrors, g => g.Name.Contains("cnpmjs"));

        foreach (var mirror in MirrorUrlService.AvailableMirrors)
        {
            Assert.StartsWith("https://", mirror.BaseUrl);
        }
    }
}
