using System.Diagnostics;
using System.Net.NetworkInformation;

namespace GithubAccelerator.Services;

public class IpSpeedTestService
{
    private const int DefaultTimeout = 3000;
    private const int PingCount = 2;

    public async Task<List<IpSpeedTestResult>> TestIpListAsync(
        List<(string ip, string domain)> ipList,
        int timeout = DefaultTimeout,
        CancellationToken cancellationToken = default)
    {
        var results = new List<IpSpeedTestResult>();
        var tasks = ipList.Select(async pair =>
        {
            return await TestSingleIpAsync(pair.ip, pair.domain, timeout, cancellationToken);
        });

        var allResults = await Task.WhenAll(tasks);
        results.AddRange(allResults.Where(r => r != null)!);
        results.Sort((a, b) => a.LatencyMs.CompareTo(b.LatencyMs));
        return results;
    }

    public async Task<IpSpeedTestResult?> TestSingleIpAsync(
        string ip,
        string domain,
        int timeout = DefaultTimeout,
        CancellationToken cancellationToken = default)
    {
        var result = new IpSpeedTestResult
        {
            Ip = ip,
            Domain = domain,
            LatencyMs = -1,
            IsReachable = false
        };

        try
        {
            using var ping = new Ping();
            var replies = new List<PingReply>();

            for (int i = 0; i < PingCount; i++)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var reply = await ping.SendPingAsync(ip, timeout, new byte[32], new PingOptions { Ttl = 128 });
                if (reply.Status == IPStatus.Success)
                {
                    replies.Add(reply);
                }
            }

            if (replies.Count > 0)
            {
                result.IsReachable = true;
                result.LatencyMs = (long)replies.Average(r => r.RoundtripTime);
            }
        }
        catch
        {
        }

        return result;
    }

    public List<(string ip, string domain)> SelectBestIps(
        List<IpSpeedTestResult> results,
        int maxPerDomain = 3)
    {
        var bestIps = new List<(string ip, string domain)>();
        var groupedByDomain = results
            .Where(r => r.IsReachable && r.LatencyMs > 0)
            .GroupBy(r => r.Domain)
            .OrderBy(g => g.Min(r => r.LatencyMs));

        foreach (var group in groupedByDomain)
        {
            var bestInGroup = group
                .OrderBy(r => r.LatencyMs)
                .Take(maxPerDomain)
                .Select(r => (r.Ip, r.Domain));

            bestIps.AddRange(bestInGroup);
        }

        return bestIps;
    }

    public string GenerateOptimizedHostsContent(
        List<(string ip, string domain)> bestIps,
        string originalContent)
    {
        var lines = new List<string>();
        lines.Add("# GitHub520 Host Start");
        lines.Add($"# Optimized at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        lines.Add($"# Total IPs: {bestIps.Count}");
        lines.Add("");

        foreach (var (ip, domain) in bestIps)
        {
            lines.Add($"{ip}\t{domain}");
        }

        lines.Add("");
        lines.Add("# GitHub520 Host End");

        return string.Join("\n", lines);
    }
}
