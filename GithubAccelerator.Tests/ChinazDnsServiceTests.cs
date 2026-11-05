using System.Net;
using System.Reflection;
using GithubAccelerator.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GithubAccelerator.Tests;

/// <summary>
/// ChinazDnsService 单元测试 - 验证 IP 过滤逻辑、缓存机制、DNS 解析结果处理
/// 注意：网络相关的实际 DNS 查询不在单元测试范围（应在集成测试中）
/// </summary>
public class ChinazDnsServiceTests : IDisposable
{
    private readonly ChinazDnsService _service;
    private readonly ILogger<ChinazDnsService> _logger;

    public ChinazDnsServiceTests()
    {
        _logger = NullLogger<ChinazDnsService>.Instance;
        // 每次测试前重置静态缓存状态
        ResetCache();
        _service = new ChinazDnsService(_logger);
    }

    public void Dispose()
    {
        _service.Dispose();
        ResetCache();
        GC.SuppressFinalize(this);
    }

    // ===== 静态缓存机制 =====

    [Fact]
    public void HasCachedContent_InitiallyFalse()
    {
        Assert.False(ChinazDnsService.HasCachedContent);
    }

    [Fact]
    public void CachedContent_WhenNoCache_ReturnsNull()
    {
        Assert.Null(ChinazDnsService.CachedContent);
    }

    [Fact]
    public void CacheAgeDescription_WhenNoCache_ReturnsSpecificText()
    {
        Assert.Equal("无缓存", ChinazDnsService.CacheAgeDescription);
    }

    [Fact]
    public void UpdateCache_SetsCachedContent()
    {
        const string testContent = "# test hosts content";
        ChinazDnsService.UpdateCache(testContent);

        Assert.True(ChinazDnsService.HasCachedContent);
        Assert.Equal(testContent, ChinazDnsService.CachedContent);
    }

    [Fact]
    public void UpdateCache_UpdatesCacheAgeDescription()
    {
        ChinazDnsService.UpdateCache("test content");

        var age = ChinazDnsService.CacheAgeDescription;
        Assert.NotEqual("无缓存", age);
        // 刚刚更新，应该是 "X 秒前"
        Assert.Contains("秒前", age);
    }

    [Fact]
    public void CacheTtl_Is5Minutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), ChinazDnsService.CacheTtl);
    }

    // ===== IP 验证逻辑（通过反射测试私有方法）=====

    [Theory]
    [InlineData("140.82.114.4", true)]      // GitHub 核心 IP
    [InlineData("185.199.109.133", true)]   // GitHub Pages CDN
    [InlineData("192.30.255.113", true)]    // GitHub 旧 IP 段
    [InlineData("20.205.243.166", true)]    // Azure/GitHub
    [InlineData("8.8.8.8", false)]          // Google DNS - 应被过滤
    [InlineData("1.1.1.1", false)]          // Cloudflare DNS - 应被过滤
    [InlineData("127.0.0.1", false)]        // 本地回环 - 应被过滤
    [InlineData("0.0.0.0", false)]          // 0.开头 - 应被过滤
    [InlineData("10.0.0.1", false)]         // 私有地址 - 应被过滤
    [InlineData("256.1.1.1", false)]        // 非法 IP
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("not.an.ip", false)]
    [InlineData("1.2.3", false)]            // 段数不对
    [InlineData("1.2.3.4.5", false)]        // 段数不对
    public void IsValidIp_VariousInputs_ReturnsExpected(string? ip, bool expected)
    {
        var result = InvokePrivateStaticMethod<bool>("IsValidIp", ip);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("140.82.114.4", true)]      // 已知 GitHub 前缀
    [InlineData("185.199.109.133", true)]   // 已知 GitHub 前缀
    [InlineData("20.205.243.166", true)]    // 已知 GitHub 前缀
    [InlineData("192.30.255.113", true)]    // 已知 GitHub 前缀
    [InlineData("151.101.1.1", true)]       // 已知 GitHub 前缀
    [InlineData("10.0.0.1", false)]         // 私有地址
    [InlineData("192.168.1.1", false)]      // 私有地址
    [InlineData("172.16.0.1", false)]       // 私有地址
    [InlineData("8.8.8.8", true)]           // 非私有但未知前缀 - 保留（可能是新 CDN）
    [InlineData("1.1.1.1", true)]           // 非私有但未知前缀 - 保留
    public void IsLikelyGitHubIp_VariousInputs_ReturnsExpected(string ip, bool expected)
    {
        var result = InvokePrivateStaticMethod<bool>("IsLikelyGitHubIp", ip);
        Assert.Equal(expected, result);
    }

    // ===== DnsResult 模型 =====

    [Fact]
    public void DnsResult_DefaultValues_AreEmpty()
    {
        var result = new DnsResult();
        Assert.Equal(string.Empty, result.Domain);
        Assert.Equal(string.Empty, result.Ip);
        Assert.Equal(string.Empty, result.Source);
        Assert.Equal(default(DateTime), result.QueryTime);
    }

    [Fact]
    public void DnsResult_CanSetProperties()
    {
        var now = DateTime.Now;
        var result = new DnsResult
        {
            Domain = "github.com",
            Ip = "140.82.114.4",
            Source = "Google-DoH",
            QueryTime = now
        };

        Assert.Equal("github.com", result.Domain);
        Assert.Equal("140.82.114.4", result.Ip);
        Assert.Equal("Google-DoH", result.Source);
        Assert.Equal(now, result.QueryTime);
    }

    // ===== DnsProvider 模型 =====

    [Fact]
    public void DnsProvider_DefaultValues_AreCorrect()
    {
        var provider = new DnsProvider();
        Assert.Equal(string.Empty, provider.Name);
        Assert.Equal(string.Empty, provider.Url);
        Assert.Equal("json", provider.Type);
        Assert.Equal(5, provider.TimeoutSeconds);
    }

    [Fact]
    public void DnsProvider_CanSetProperties()
    {
        var provider = new DnsProvider
        {
            Name = "TestDNS",
            Url = "https://test.example.com/dns-query",
            Type = "custom",
            TimeoutSeconds = 10
        };

        Assert.Equal("TestDNS", provider.Name);
        Assert.Equal("https://test.example.com/dns-query", provider.Url);
        Assert.Equal("custom", provider.Type);
        Assert.Equal(10, provider.TimeoutSeconds);
    }

    // ===== 单例 =====

    [Fact]
    public void Instance_ReturnsSameInstance()
    {
        var inst1 = ChinazDnsService.Instance;
        var inst2 = ChinazDnsService.Instance;
        Assert.Same(inst1, inst2);
    }

    // ===== Dispose =====

    [Fact]
    public void Dispose_CalledOnce_DoesNotThrow()
    {
        var service = new ChinazDnsService(_logger);
        var exception = Record.Exception(() => service.Dispose());
        Assert.Null(exception);
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_DoesNotThrow()
    {
        var service = new ChinazDnsService(_logger);
        service.Dispose();
        service.Dispose();
        // 第二次 Dispose 不应抛出异常
    }

    // ===== 辅助方法 =====

    private static void ResetCache()
    {
        // 通过反射重置静态缓存字段（避免测试间相互影响）
        var flags = BindingFlags.NonPublic | BindingFlags.Static;
        typeof(ChinazDnsService)
            .GetField("_cachedHostsContent", flags)
            ?.SetValue(null, null);
        typeof(ChinazDnsService)
            .GetField("_cacheGeneratedTime", flags)
            ?.SetValue(null, DateTime.MinValue);
    }

    private static T InvokePrivateStaticMethod<T>(string methodName, params object?[]? args)
    {
        var method = typeof(ChinazDnsService).GetMethod(methodName,
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var result = method!.Invoke(null, args ?? Array.Empty<object?>());
        Assert.NotNull(result);
        return (T)result;
    }
}
