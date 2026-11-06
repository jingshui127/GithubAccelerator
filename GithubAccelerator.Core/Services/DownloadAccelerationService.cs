using System;
using System.Collections.Generic;
using System.Linq;

namespace GithubAccelerator.Core.Services;

public interface IDownloadAccelerationService
{
    string AccelerateUrl(string url);
    string[] GetAcceleratedUrls(string url);
    bool IsGithubUrl(string url);
    bool IsGithubReleaseUrl(string url);
    bool IsGithubRawUrl(string url);
}

/// <summary>
/// URL 前缀型下载加速。镜像地址统一来自 <see cref="MirrorUrlService.AvailableMirrors"/>，
/// 避免这里再维护一份互不一致、包含已停服站点的列表。
/// </summary>
public class DownloadAccelerationService : IDownloadAccelerationService
{
    private static readonly MirrorSite[] ProxyMirrors = MirrorUrlService.AvailableMirrors
        .Where(m => m.UrlTemplate != null)
        .ToArray();

    private static readonly string[] SupportedHosts =
    {
        "github.com",
        "raw.githubusercontent.com",
        "githubusercontent.com",
        "github-releases.githubusercontent.com",
        "codeload.github.com"
    };

    public string AccelerateUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return url;

        var accelerated = GetAcceleratedUrls(url);
        return accelerated.Length > 1 ? accelerated[1] : url;
    }

    public string[] GetAcceleratedUrls(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return new[] { url };

        var results = new List<string> { url };

        try
        {
            var uri = new Uri(url);
            var host = uri.Host.ToLowerInvariant();
            var matched = MatchedHost(host);
            if (matched == null) return results.ToArray();

            foreach (var mirror in ProxyMirrors)
            {
                var target = $"https://{host}{uri.PathAndQuery}";
                results.Add(mirror.UrlTemplate!.Replace("{url}", target));
            }
        }
        catch
        {
            // Invalid URL
        }

        return results.ToArray();
    }

    private static string? MatchedHost(string host)
    {
        foreach (var candidate in SupportedHosts)
        {
            if (host == candidate || host.EndsWith("." + candidate))
            {
                return candidate;
            }
        }
        return null;
    }

    public bool IsGithubUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        try
        {
            var uri = new Uri(url);
            var host = uri.Host.ToLower();
            return host.Contains("github") || host.EndsWith("github.com");
        }
        catch
        {
            return false;
        }
    }

    public bool IsGithubReleaseUrl(string url)
    {
        if (!IsGithubUrl(url))
            return false;

        try
        {
            var uri = new Uri(url);
            return uri.PathAndQuery.Contains("/releases/download/");
        }
        catch
        {
            return false;
        }
    }

    public bool IsGithubRawUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        try
        {
            var uri = new Uri(url);
            var host = uri.Host.ToLower();
            return host == "raw.githubusercontent.com" || host.EndsWith("githubusercontent.com");
        }
        catch
        {
            return false;
        }
    }
}
