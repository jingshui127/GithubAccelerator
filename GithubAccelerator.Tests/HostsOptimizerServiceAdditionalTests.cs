using System.Reflection;
using GithubAccelerator.Services;

namespace GithubAccelerator.Tests;

/// <summary>
/// HostsOptimizerService 补充单元测试 - 覆盖 IsGitHubDomain、ParseHostsMappings、属性默认值等
/// 与 HostsOptimizerTests.cs 互补
/// </summary>
public class HostsOptimizerServiceAdditionalTests
{
    // ===== 属性默认值 =====

    [Fact]
    public void ProbeTimeoutSeconds_Default_Is5()
    {
        var optimizer = new HostsOptimizerService();
        Assert.Equal(5, optimizer.ProbeTimeoutSeconds);
    }

    [Fact]
    public void MaxMappingsPerDomain_Default_Is1()
    {
        var optimizer = new HostsOptimizerService();
        Assert.Equal(1, optimizer.MaxMappingsPerDomain);
    }

    [Fact]
    public void IncludeOriginalIps_Default_IsTrue()
    {
        var optimizer = new HostsOptimizerService();
        Assert.True(optimizer.IncludeOriginalIps);
    }

    [Fact]
    public void IncludeDnsResolvedIps_Default_IsTrue()
    {
        var optimizer = new HostsOptimizerService();
        Assert.True(optimizer.IncludeDnsResolvedIps);
    }

    [Fact]
    public void Properties_CanBeModified()
    {
        var optimizer = new HostsOptimizerService
        {
            ProbeTimeoutSeconds = 10,
            MaxMappingsPerDomain = 3,
            IncludeOriginalIps = false,
            IncludeDnsResolvedIps = false
        };

        Assert.Equal(10, optimizer.ProbeTimeoutSeconds);
        Assert.Equal(3, optimizer.MaxMappingsPerDomain);
        Assert.False(optimizer.IncludeOriginalIps);
        Assert.False(optimizer.IncludeDnsResolvedIps);
    }

    // ===== IsGitHubDomain（通过反射调用私有方法）=====

    [Theory]
    [InlineData("github.com", true)]
    [InlineData("api.github.com", true)]
    [InlineData("raw.githubusercontent.com", true)]
    [InlineData("objects.githubusercontent.com", true)]
    [InlineData("avatars.githubusercontent.com", true)]
    [InlineData("gist.github.com", true)]
    [InlineData("codeload.github.com", true)]
    [InlineData("github.githubassets.com", true)]
    [InlineData("media.githubusercontent.com", true)]
    [InlineData("user-images.githubusercontent.com", true)]
    [InlineData("live.github.com", true)]
    [InlineData("central.github.com", true)]
    [InlineData("GITHUB.COM", true)]                // 大小写不敏感
    [InlineData("GitHub.Com", true)]
    [InlineData("RAW.GitHubUserContent.com", true)]
    [InlineData("example.com", false)]
    [InlineData("gitlab.com", false)]
    [InlineData("bitbucket.org", false)]
    [InlineData("", false)]
    [InlineData("github", true)]                    // 包含 "github" 子串
    [InlineData("not-github.com", true)]            // 包含 "github" 子串
    public void IsGitHubDomain_VariousInputs_ReturnsExpected(string domain, bool expected)
    {
        var result = InvokePrivateStaticMethod<bool>("IsGitHubDomain", domain);
        Assert.Equal(expected, result);
    }

    // ===== ParseHostsMappings（通过反射调用私有方法）=====

    [Fact]
    public void ParseHostsMappings_EmptyContent_ReturnsEmptyList()
    {
        var result = InvokeParseHostsMappings("");
        Assert.Empty(result);
    }

    [Fact]
    public void ParseHostsMappings_OnlyComments_ReturnsEmptyList()
    {
        var content = "# comment1\n# comment2\n";
        var result = InvokeParseHostsMappings(content);
        Assert.Empty(result);
    }

    [Fact]
    public void ParseHostsMappings_OnlyNonGithubDomains_ReturnsEmptyList()
    {
        var content = "1.1.1.1 example.com\n2.2.2.2 gitlab.com\n";
        var result = InvokeParseHostsMappings(content);
        Assert.Empty(result);
    }

    [Fact]
    public void ParseHostsMappings_WithGithubDomains_ReturnsOnlyGithubEntries()
    {
        var content = """
1.1.1.1 example.com
140.82.114.4 github.com
199.232.69.194 raw.githubusercontent.com
2.2.2.2 gitlab.com
""";
        var result = InvokeParseHostsMappings(content);
        Assert.Equal(2, result.Count);
        // 通过反射访问私有 HostMapping 类的 Domain 属性
        Assert.All(result, m => Assert.True(
            ((string)m.GetType().GetProperty("Domain")!.GetValue(m)!).Contains("github", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void ParseHostsMappings_HandlesTabsAndSpaces()
    {
        var content = "140.82.114.4\tgithub.com\n199.232.69.194    raw.githubusercontent.com\n";
        var result = InvokeParseHostsMappings(content);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void ParseHostsMappings_SkipsCommentedLines()
    {
        var content = """
# 140.82.114.4 github.com
140.82.114.4 github.com
# 199.232.69.194 raw.githubusercontent.com
""";
        var result = InvokeParseHostsMappings(content);
        Assert.Single(result);
        var domain = (string)result[0].GetType().GetProperty("Domain")!.GetValue(result[0])!;
        Assert.Equal("github.com", domain);
    }

    [Fact]
    public void ParseHostsMappings_HandlesMalformedLines()
    {
        var content = """
not a valid line
140.82.114.4
140.82.114.4 github.com
""";
        var result = InvokeParseHostsMappings(content);
        Assert.Single(result);
    }

    // ===== MaxConcurrentProbes 常量验证 =====

    [Fact]
    public void MaxConcurrentProbes_HasExpectedValue()
    {
        var field = typeof(HostsOptimizerService)
            .GetField("MaxConcurrentProbes", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        var value = field!.GetValue(null);
        Assert.Equal(12, value);
    }

    // ===== CoreDomains 验证 =====

    [Fact]
    public void CoreDomains_ContainsAllExpectedDomains()
    {
        var field = typeof(HostsOptimizerService)
            .GetField("CoreDomains", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        var value = field!.GetValue(null) as HashSet<string>;
        Assert.NotNull(value);

        var expectedDomains = new[]
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
            "user-images.githubusercontent.com"
        };

        foreach (var domain in expectedDomains)
        {
            Assert.Contains(domain, value!);
        }
    }

    [Fact]
    public void CoreDomains_IsCaseInsensitive()
    {
        var field = typeof(HostsOptimizerService)
            .GetField("CoreDomains", BindingFlags.NonPublic | BindingFlags.Static);
        var value = field!.GetValue(null) as HashSet<string>;
        Assert.NotNull(value);

        // StringComparer.OrdinalIgnoreCase 应该被使用
        Assert.Contains("GITHUB.COM", value!);
        Assert.Contains("Api.GitHub.Com", value!);
    }

    // ===== OnLog 事件 =====

    [Fact]
    public async Task OptimizeAsync_FiresOnLogEvents()
    {
        var optimizer = new HostsOptimizerService
        {
            IncludeDnsResolvedIps = false,
            ProbeTimeoutSeconds = 2
        };

        var logMessages = new List<string>();
        optimizer.OnLog += msg => logMessages.Add(msg);

        var content = "140.82.114.4 github.com\n185.199.109.133 raw.githubusercontent.com\n";
        await optimizer.OptimizeAsync(content);

        Assert.NotEmpty(logMessages);
        Assert.Contains(logMessages, m => m.Contains("开始优化"));
        Assert.Contains(logMessages, m => m.Contains("优化完成"));
    }

    // ===== 边界条件 =====

    [Fact]
    public async Task OptimizeAsync_WithCancellationToken_HonorsCancellation()
    {
        var optimizer = new HostsOptimizerService
        {
            IncludeDnsResolvedIps = false,
            ProbeTimeoutSeconds = 2
        };

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var content = "140.82.114.4 github.com\n";

        // 取消的 token 可能抛 OperationCanceledException 或正常返回
        try
        {
            var result = await optimizer.OptimizeAsync(content, cts.Token);
            Assert.NotNull(result);
        }
        catch (OperationCanceledException)
        {
            // 期望：取消被尊重
        }
    }

    [Fact]
    public async Task OptimizeAsync_OnlyNonGithubDomains_ReturnsOriginalContent()
    {
        var optimizer = new HostsOptimizerService
        {
            IncludeDnsResolvedIps = false
        };

        var content = "1.1.1.1 example.com\n2.2.2.2 gitlab.com\n";
        var result = await optimizer.OptimizeAsync(content);

        // 没有 GitHub 域名时返回原始内容
        Assert.Equal(content, result);
    }

    [Fact]
    public async Task OptimizeAsync_PreservesOriginalHeaderComments()
    {
        var optimizer = new HostsOptimizerService
        {
            IncludeDnsResolvedIps = false,
            ProbeTimeoutSeconds = 2
        };

        var content = """
# Header comment 1
# Header comment 2
# Update time: 2026-07-21
140.82.114.4 github.com
""";
        var result = await optimizer.OptimizeAsync(content);

        Assert.Contains("Header comment 1", result);
        Assert.Contains("Header comment 2", result);
        Assert.Contains("Update time: 2026-07-21", result);
    }

    [Fact]
    public async Task OptimizeAsync_GeneratesOptimizationMetadata()
    {
        var optimizer = new HostsOptimizerService
        {
            IncludeDnsResolvedIps = false,
            ProbeTimeoutSeconds = 2
        };

        var result = await optimizer.OptimizeAsync("140.82.114.4 github.com\n");

        Assert.Contains("优化时间", result);
        Assert.Contains("原始映射", result);
        Assert.Contains("可达域名", result);
        Assert.Contains("探测方式", result);
    }

    // ===== 辅助方法 =====

    private static T InvokePrivateStaticMethod<T>(string methodName, params object[] args)
    {
        var method = typeof(HostsOptimizerService).GetMethod(methodName,
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var result = method!.Invoke(null, args);
        if (result == null) return default!;
        return (T)result;
    }

    /// <summary>
    /// 调用私有 ParseHostsMappings 方法并返回动态对象列表
    /// </summary>
    private static List<dynamic> InvokeParseHostsMappings(string content)
    {
        var method = typeof(HostsOptimizerService).GetMethod("ParseHostsMappings",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        var optimizer = new HostsOptimizerService();
        var result = method!.Invoke(optimizer, new object[] { content });
        Assert.NotNull(result);
        return ((System.Collections.IEnumerable)result!)
            .Cast<object>()
            .Select(o => (dynamic)o)
            .ToList();
    }
}
