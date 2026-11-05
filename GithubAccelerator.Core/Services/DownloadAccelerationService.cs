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

public class DownloadAccelerationService : IDownloadAccelerationService
{
    private readonly Dictionary<string, string[]> _mirrors = new()
    {
        { "github.com", new[] { "ghproxy.net", "gh.api.99988866.xyz" } },
        { "raw.githubusercontent.com", new[] { "raw.fastgit.org", "raw.gitmirror.com", "raw.sevencdn.com" } },
        { "githubusercontent.com", new[] { "ghproxy.net" } },
        { "github-releases.githubusercontent.com", new[] { "ghproxy.net" } }
    };

    public string AccelerateUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return url;

        try
        {
            var uri = new Uri(url);
            var host = uri.Host.ToLower();

            if (_mirrors.TryGetValue(host, out var mirrors))
            {
                return $"https://{mirrors[0]}/https://{host}{uri.PathAndQuery}";
            }

            foreach (var (originalHost, mirrorList) in _mirrors)
            {
                if (host.EndsWith(originalHost))
                {
                    return $"https://{mirrorList[0]}/https://{host}{uri.PathAndQuery}";
                }
            }
        }
        catch
        {
            // Invalid URL, return original
        }

        return url;
    }

    public string[] GetAcceleratedUrls(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return new[] { url };

        var results = new List<string> { url };

        try
        {
            var uri = new Uri(url);
            var host = uri.Host.ToLower();

            if (_mirrors.TryGetValue(host, out var mirrors))
            {
                results.AddRange(mirrors.Select(m => $"https://{m}/https://{host}{uri.PathAndQuery}"));
            }
            else
            {
                foreach (var (originalHost, mirrorList) in _mirrors)
                {
                    if (host.EndsWith(originalHost))
                    {
                        results.AddRange(mirrorList.Select(m => $"https://{m}/https://{host}{uri.PathAndQuery}"));
                        break;
                    }
                }
            }
        }
        catch
        {
            // Invalid URL
        }

        return results.ToArray();
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
