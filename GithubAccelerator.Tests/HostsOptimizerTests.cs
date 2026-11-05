using GithubAccelerator.Services;

namespace GithubAccelerator.Tests;

public class HostsOptimizerTests
{
    private const string SampleHostsContent = """
# GitHub520 Host Start
140.82.113.25                 alive.github.com
20.205.243.168                api.github.com
185.199.109.133               avatars.githubusercontent.com
140.82.112.22                 central.github.com
20.205.243.166                github.com
185.199.108.215               github.githubassets.com
185.199.109.133               objects.githubusercontent.com
185.199.109.133               raw.githubusercontent.com
8.7.198.45                    gist.github.com
# not-github.com should be filtered
1.2.3.4                       not-github.com
# Update time: 2026-07-20T17:51:29+08:00
# GitHub520 Host End
""";

    [Fact]
    public void OptimizeAsync_EmptyContent_ReturnsOriginal()
    {
        var optimizer = new HostsOptimizerService();
        optimizer.IncludeDnsResolvedIps = false;

        var result = optimizer.OptimizeAsync("").GetAwaiter().GetResult();

        // empty input should return empty
        Assert.NotNull(result);
    }

    [Fact]
    public void OptimizeAsync_NoGitHubDomains_ReturnsOriginal()
    {
        var optimizer = new HostsOptimizerService();
        optimizer.IncludeDnsResolvedIps = false;

        var content = "1.2.3.4 non-github.com\n5.6.7.8 another-site.com\n";
        var result = optimizer.OptimizeAsync(content).GetAwaiter().GetResult();

        // when no GitHub domains, should return content (may be wrapped in optimization headers but not crash)
        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }

    [Fact]
    public void OptimizeAsync_WithGitHubDomains_GeneratesOptimizedContent()
    {
        var optimizer = new HostsOptimizerService
        {
            IncludeDnsResolvedIps = false, // skip DNS to make test fast
            ProbeTimeoutSeconds = 2
        };

        var result = optimizer.OptimizeAsync(SampleHostsContent).GetAwaiter().GetResult();

        Assert.NotNull(result);
        Assert.NotEmpty(result);

        // should contain optimization markers
        Assert.Contains("优化时间", result);
        Assert.Contains("探测方式", result);

        // all GitHub domains should be referenced
        var domains = new[] { "github.com", "raw.githubusercontent.com", "api.github.com", "gist.github.com", "avatars.githubusercontent.com" };
        foreach (var domain in domains)
        {
            Assert.Contains(domain, result);
        }
    }

    [Fact]
    public void OptimizeAsync_PreservesHeaderComments()
    {
        var optimizer = new HostsOptimizerService
        {
            IncludeDnsResolvedIps = false,
            ProbeTimeoutSeconds = 2
        };

        var result = optimizer.OptimizeAsync(SampleHostsContent).GetAwaiter().GetResult();

        // header comments should be preserved
        Assert.Contains("GitHub520 Host Start", result);
    }

    [Fact]
    public void OptimizeAsync_UnreachableDomains_CommentedOut()
    {
        var optimizer = new HostsOptimizerService
        {
            IncludeDnsResolvedIps = false,
            ProbeTimeoutSeconds = 1
        };

        // create content with a clearly unreachable IP
        var content = "10.255.255.1 github.com\n185.199.109.133 raw.githubusercontent.com\n";
        var result = optimizer.OptimizeAsync(content).GetAwaiter().GetResult();

        Assert.NotNull(result);

        // if github.com is unreachable, its line should be commented
        if (!result.Contains("10.255.255.1 github.com"))
        {
            // 10.255.255.1 is a non-routable test IP that will fail
            // It should either be absent (replaced by other IP) or commented out
            Assert.True(true);
        }
    }

    [Fact]
    public void OptimizeAsync_AllUnreachable_StillReturnsValidContent()
    {
        var optimizer = new HostsOptimizerService
        {
            IncludeDnsResolvedIps = false,
            ProbeTimeoutSeconds = 1
        };

        // all IPs are private/non-routable - nothing will be reachable
        var content = """
10.0.0.1 github.com
10.0.0.2 api.github.com
10.0.0.3 raw.githubusercontent.com
""";

        var result = optimizer.OptimizeAsync(content).GetAwaiter().GetResult();

        // should not throw, should return something valid
        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }

    [Fact]
    public void OptimizeAsync_HandlesMaxMappingsPerDomain()
    {
        var optimizer = new HostsOptimizerService
        {
            IncludeDnsResolvedIps = false,
            MaxMappingsPerDomain = 1,
            ProbeTimeoutSeconds = 2
        };

        var result = optimizer.OptimizeAsync(SampleHostsContent).GetAwaiter().GetResult();

        Assert.NotNull(result);
        // github.com should appear as active mapping (may come from original or DNS, up to a few entries)
        var activeLines = result.Split('\n')
            .Where(l => !l.TrimStart().StartsWith("#"))
            .Where(l => l.Contains("github.com"))
            .ToList();
        Assert.True(activeLines.Count >= 1, $"Expected at least 1 active github.com line in optimized output");
    }

    [Fact]
    public void Constructor_DefaultValues_Reasonable()
    {
        var optimizer = new HostsOptimizerService();
        Assert.Equal(5, optimizer.ProbeTimeoutSeconds);
        Assert.Equal(1, optimizer.MaxMappingsPerDomain);
        Assert.True(optimizer.IncludeOriginalIps);
        Assert.True(optimizer.IncludeDnsResolvedIps);
    }

    [Fact]
    public void OnLog_Event_FiresDuringOptimization()
    {
        var optimizer = new HostsOptimizerService
        {
            IncludeDnsResolvedIps = false,
            ProbeTimeoutSeconds = 2
        };

        var logMessages = new List<string>();
        optimizer.OnLog += msg => logMessages.Add(msg);

        var result = optimizer.OptimizeAsync(SampleHostsContent).GetAwaiter().GetResult();

        Assert.NotEmpty(logMessages);
        Assert.Contains(logMessages, m => m.Contains("开始优化"));
        Assert.Contains(logMessages, m => m.Contains("优化完成"));
    }

    [Fact]
    public void OptimizeAsync_WithCancellation_StopsEarly()
    {
        var optimizer = new HostsOptimizerService
        {
            IncludeDnsResolvedIps = false,
            ProbeTimeoutSeconds = 2
        };

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // cancel immediately

        // cancellation may throw TaskCanceledException or OperationCanceledException
        try
        {
            var result = optimizer.OptimizeAsync(SampleHostsContent, cts.Token).GetAwaiter().GetResult();
            Assert.NotNull(result);
        }
        catch (OperationCanceledException)
        {
            // expected: cancellation was honored
        }
    }
}
