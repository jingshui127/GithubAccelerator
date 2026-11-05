namespace GithubAccelerator.Services;

/// <summary>
/// IP 测速结果
/// </summary>
public class IpSpeedTestResult
{
    public string Ip { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public long LatencyMs { get; set; }
    public bool IsReachable { get; set; }
}
