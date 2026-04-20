using System.Net.Http;
using System.Text.RegularExpressions;

namespace GithubAccelerator.Services;

public class GithubHostsService
{
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private const string HostsSourceUrl = "https://raw.hellogithub.com/hosts";
    private const string BackupSourceUrl = "https://raw.githubusercontent.com/maxiaof/github-hosts/master/hosts";

    public async Task<string> FetchHostsAsync()
    {
        var hosts = await TryFetchFromSourceAsync(HostsSourceUrl);
        if (!string.IsNullOrEmpty(hosts))
            return hosts;

        hosts = await TryFetchFromSourceAsync(BackupSourceUrl);
        if (!string.IsNullOrEmpty(hosts))
            return hosts;

        throw new Exception("无法获取GitHub Hosts数据，请检查网络连接。");
    }

    private async Task<string> TryFetchFromSourceAsync(string url)
    {
        try
        {
            var response = await _httpClient.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                if (ContainsGithubHosts(content))
                    return content;
            }
        }
        catch
        {
        }
        return string.Empty;
    }

    private bool ContainsGithubHosts(string content)
    {
        return content.Contains("github.com") &&
               content.Contains("raw.githubusercontent.com");
    }

    public List<(string ip, string domain)> ParseHosts(string hostsContent)
    {
        var result = new List<(string ip, string domain)>();
        var lines = hostsContent.Split('\n');

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith('#'))
                continue;

            var match = Regex.Match(trimmedLine, @"^(\d+\.\d+\.\d+\.\d+)\s+(\S+)$");
            if (match.Success)
            {
                var ip = match.Groups[1].Value;
                var domain = match.Groups[2].Value;
                result.Add((ip, domain));
            }
        }

        return result;
    }

    public DateTime? GetUpdateTime(string hostsContent)
    {
        var match = Regex.Match(hostsContent, @"Update time:\s*(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})");
        if (match.Success && DateTime.TryParse(match.Groups[1].Value, out var dateTime))
            return dateTime;
        return null;
    }
}