using System;

namespace GithubAccelerator.Services;

/// <summary>
/// “拉取 + 线路优选”结果的内存缓存：带时间戳与新鲜度，避免每次点击都从零开始等待网络探测。
/// <para>
/// 缓存只影响“选出候选线路”这一段；写入前的 20% 准入门槛、先判后提权、Hosts 写入边界完全不受影响。
/// 默认新鲜度 10 分钟：GitHub 源 IP 在该时间尺度内基本稳定；即使 IP 恰好已失效，后续准入门槛的连接预筛与整页配对
/// 仍会把它拦下（不会误写），只是表现为一次拒绝。
/// </para>
/// </summary>
public sealed class HostsSelectionCache
{
    /// <summary>默认新鲜度：10 分钟内再次点击直接复用上次选出的线路。</summary>
    public static readonly TimeSpan DefaultFreshness = TimeSpan.FromMinutes(10);

    private readonly object _gate = new();
    private Snapshot? _snapshot;

    /// <summary>一次“已选出线路”的快照。</summary>
    public sealed record Snapshot(string Content, int SourceCount, DateTimeOffset SelectedAt);

    /// <summary>取新鲜快照；过期或从未写入时返回 null。</summary>
    public Snapshot? GetFresh(DateTimeOffset now, TimeSpan? freshness = null)
    {
        lock (_gate)
        {
            if (_snapshot == null) return null;
            var window = freshness ?? DefaultFreshness;
            return now - _snapshot.SelectedAt <= window ? _snapshot : null;
        }
    }

    /// <summary>无论是否过期都返回上次快照（预热判断用）。</summary>
    public Snapshot? Peek()
    {
        lock (_gate) return _snapshot;
    }

    public void Set(string content, int sourceCount, DateTimeOffset selectedAt)
    {
        lock (_gate) _snapshot = new Snapshot(content, sourceCount, selectedAt);
    }

    public void Clear()
    {
        lock (_gate) _snapshot = null;
    }
}
