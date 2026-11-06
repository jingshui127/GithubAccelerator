using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GithubAccelerator.Core.Services;
using Xunit;

namespace GithubAccelerator.Tests;

/// <summary>
/// 镜像版链接（打开页面）的决策、不可加速域名的明确提示，以及四类下载路径的镜像覆盖。
/// 全部为纯逻辑测试，不访问网络。
/// </summary>
public class MirrorLinkAndCoverageTests
{
    [Fact]
    public void ConvertUrl_ApiHost_IsMarkedUnsupportedWithReason()
    {
        var result = new MirrorUrlService().ConvertUrl("https://api.github.com/repos/cli/cli/releases");

        Assert.True(result.IsUnsupported);
        Assert.False(result.HasResults);
        Assert.Contains("api.github.com", result.UnsupportedReason);
        Assert.Contains("直连可用", result.UnsupportedReason);
    }

    [Fact]
    public void ConvertUrl_GithubPage_ProducesMirrorPages()
    {
        var result = new MirrorUrlService().ConvertUrl("https://github.com/cli/cli");

        Assert.Equal(GithubUrlType.Repository, result.UrlType);
        Assert.True(result.MirrorUrls.Count >= 3);
        Assert.All(result.MirrorUrls, m => Assert.StartsWith("https://", m.Url));
    }

    [Fact]
    public void ConvertUrl_CodeloadArchive_ProducesMirrors()
    {
        var result = new MirrorUrlService().ConvertUrl("https://codeload.github.com/cli/cli/tar.gz/refs/heads/trunk");

        Assert.Equal(GithubUrlType.ReleaseOrZip, result.UrlType);
        Assert.NotEmpty(result.MirrorUrls);
    }

    [Fact]
    public void ConvertUrl_RawFile_ProducesMirrorsAndJsDelivr()
    {
        var result = new MirrorUrlService().ConvertUrl("https://raw.githubusercontent.com/octocat/Hello-World/master/README");

        Assert.Equal(GithubUrlType.RawFile, result.UrlType);
        Assert.NotEmpty(result.MirrorUrls);
        Assert.True(result.HasJsDelivrUrl);
    }

    [Fact]
    public void Plan_HealthyMirror_OpensFirstAndStatesItIsNotGithub()
    {
        var result = new MirrorResult
        {
            OriginalUrl = "https://github.com/cli/cli",
            AnyHealthyMirror = true
        };
        result.MirrorUrls.Add(new ConvertedUrl("ghfast.top", "https://ghfast.top/https://github.com/cli/cli", ""));

        var plan = MirrorOpenPlanner.Plan(result);

        Assert.True(plan.CanOpen);
        Assert.Equal("https://ghfast.top/https://github.com/cli/cli", plan.Url);
        Assert.Contains("不是访问 github.com", plan.Message);
        Assert.False(plan.OfferOriginalLink);
    }

    [Fact]
    public void Plan_AllSourcesUnavailable_RefusesToOpenAndOffersOriginal()
    {
        var checkedAt = new DateTimeOffset(2026, 11, 5, 16, 30, 0, TimeSpan.FromHours(8));
        var result = new MirrorResult
        {
            OriginalUrl = "https://github.com/cli/cli",
            AnyHealthyMirror = false,
            HealthCheckedAt = checkedAt,
            HealthMessage = "所有加速镜像本次均不可用：ghfast.top：请求超时"
        };
        result.MirrorUrls.Add(new ConvertedUrl("ghfast.top", "https://ghfast.top/https://github.com/cli/cli", ""));

        var plan = MirrorOpenPlanner.Plan(result);

        Assert.False(plan.CanOpen);
        Assert.Null(plan.Url);
        Assert.True(plan.OfferOriginalLink);
        Assert.Contains("2026-11-05 16:30", plan.Message);
        Assert.Contains("原始链接", plan.Message);
    }

    [Fact]
    public void Plan_UnsupportedHost_ExplainsAndOffersOriginal()
    {
        var result = new MirrorUrlService().ConvertUrl("https://api.github.com/repos/cli/cli");

        var plan = MirrorOpenPlanner.Plan(result);

        Assert.False(plan.CanOpen);
        Assert.True(plan.OfferOriginalLink);
        Assert.Contains("无需加速", plan.Message);
    }

    [Fact]
    public void AvailableMirrors_DemotesGhproxyToLastWithTimeoutWarning()
    {
        var mirrors = MirrorUrlService.AvailableMirrors;

        Assert.Equal("GHProxy", mirrors[^1].Name);
        Assert.Contains("不建议使用", mirrors[^1].Description);
        Assert.Contains("69.66s", mirrors[^1].Description);
        Assert.Equal("ghproxy.net", MirrorUrlService.GitCloneMirrors[^1].Name);
    }

    [Fact]
    public void GhfastDescription_DoesNotClaimFullVerification()
    {
        var ghfast = MirrorUrlService.AvailableMirrors.First(m => m.Name == "ghfast.top");

        Assert.Contains("未做完整性", ghfast.Description);
        Assert.DoesNotContain("实测可用", ghfast.Description);
    }

    [Fact]
    public void Plan_PagePathUnhealthy_DisablesEntryAndOffersOriginal()
    {
        var checkedAt = new DateTimeOffset(2026, 11, 6, 13, 40, 0, TimeSpan.FromHours(8));
        var result = new MirrorResult
        {
            OriginalUrl = "https://github.com/cli/cli",
            AnyHealthyMirror = true,
            HealthCheckedAt = checkedAt
        };
        result.MirrorUrls.Add(new ConvertedUrl("ghfast.top", "https://ghfast.top/https://github.com/cli/cli", ""));
        result.PageHealth.Add(new MirrorPageHealth("ghfast.top",
            "https://ghfast.top/https://github.com/cli/cli", false, 100, checkedAt, "HTTP 403"));

        var plan = MirrorOpenPlanner.Plan(result);

        Assert.False(plan.CanOpen);
        Assert.Null(plan.Url);
        Assert.True(plan.OfferOriginalLink);
        Assert.Contains("2026-11-06 13:40", plan.Message);
        Assert.Contains("HTTP 403", plan.Message);
        Assert.Contains("视当时网络而定", plan.Message);
    }

    [Fact]
    public void Plan_PagePathHealthy_OpensHealthiestPageMirror()
    {
        var checkedAt = new DateTimeOffset(2026, 11, 6, 13, 40, 0, TimeSpan.FromHours(8));
        var result = new MirrorResult { OriginalUrl = "https://github.com/cli/cli" };
        result.MirrorUrls.Add(new ConvertedUrl("ghfast.top", "https://ghfast.top/https://github.com/cli/cli", ""));
        result.MirrorUrls.Add(new ConvertedUrl("gh.api.99988866.xyz",
            "https://gh.api.99988866.xyz/https://github.com/cli/cli", ""));
        result.PageHealth.Add(new MirrorPageHealth("ghfast.top",
            "https://ghfast.top/https://github.com/cli/cli", false, 80, checkedAt, "HTTP 403"));
        result.PageHealth.Add(new MirrorPageHealth("gh.api.99988866.xyz",
            "https://gh.api.99988866.xyz/https://github.com/cli/cli", true, 200, checkedAt, null));

        var plan = MirrorOpenPlanner.Plan(result);

        Assert.True(plan.CanOpen);
        Assert.Equal("https://gh.api.99988866.xyz/https://github.com/cli/cli", plan.Url);
        Assert.Contains("不是访问 github.com", plan.Message);
        Assert.False(plan.OfferOriginalLink);
    }

    [Fact]
    public void Plan_UnknownLinkWithoutMirrors_OffersOriginal()
    {
        var result = new MirrorResult { OriginalUrl = "https://example.com/file" };

        var plan = MirrorOpenPlanner.Plan(result);

        Assert.False(plan.CanOpen);
        Assert.True(plan.OfferOriginalLink);
        Assert.Contains("没有可用的镜像地址", plan.Message);
    }

    [Fact]
    public void DownloadAcceleration_CoversCodeloadAndReleaseRedirectHosts()
    {
        var service = new DownloadAccelerationService();

        var codeload = service.GetAcceleratedUrls("https://codeload.github.com/cli/cli/tar.gz/refs/heads/trunk");
        Assert.True(codeload.Length > 1, "codeload 归档应生成镜像地址");
        Assert.All(codeload.Skip(1), u => Assert.StartsWith("https://gh", u));

        // releases/download 会 302 到 objects.githubusercontent.com，必须同样可改写
        var objects = service.GetAcceleratedUrls("https://objects.githubusercontent.com/github-production-release-asset-2e65be/123");
        Assert.True(objects.Length > 1, "objects.githubusercontent.com 应生成镜像地址");
    }

    [Fact]
    public void DownloadAcceleration_CoversRawAndGithubHosts()
    {
        var service = new DownloadAccelerationService();

        Assert.True(service.GetAcceleratedUrls("https://raw.githubusercontent.com/octocat/Hello-World/master/README").Length > 1);
        Assert.True(service.GetAcceleratedUrls("https://github.com/cli/cli/releases/download/v2.40.0/gh.zip").Length > 1);
    }
}
