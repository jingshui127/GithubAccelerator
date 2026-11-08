using System;
using System.Linq;
using System.Threading.Tasks;
using GithubAccelerator.Services;
using Xunit;

namespace GithubAccelerator.Tests;

/// <summary>
/// “拉取 + 线路优选”缓存：只决定是否复用上次选出的线路，不参与 20% 门槛判定。
/// 覆盖：未写入、新鲜命中、过期不命中、Clear、覆盖写入、并发读写。
/// </summary>
public class HostsSelectionCacheTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 8, 12, 0, 0, TimeSpan.FromHours(8));

    private const string Content = "20.27.177.113 github.com\n";

    [Fact]
    public void 未写入时取不到快照()
    {
        var cache = new HostsSelectionCache();

        Assert.Null(cache.GetFresh(Now));
        Assert.Null(cache.Peek());
    }

    [Fact]
    public void 新鲜期内命中并带回内容与源数()
    {
        var cache = new HostsSelectionCache();
        cache.Set(Content, 3, Now);

        var snapshot = cache.GetFresh(Now.AddMinutes(9));

        Assert.NotNull(snapshot);
        Assert.Equal(Content, snapshot!.Content);
        Assert.Equal(3, snapshot.SourceCount);
        Assert.Contains("github.com", snapshot.Content);
    }

    [Fact]
    public void 超过新鲜度不命中但Peek仍能看到()
    {
        var cache = new HostsSelectionCache();
        cache.Set(Content, 3, Now);

        Assert.Null(cache.GetFresh(Now.Add(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1))));

        var stale = cache.Peek();
        Assert.NotNull(stale);
        Assert.Equal(Now, stale!.SelectedAt);
    }

    [Fact]
    public void 自定义新鲜度生效()
    {
        var cache = new HostsSelectionCache();
        cache.Set(Content, 3, Now);

        Assert.NotNull(cache.GetFresh(Now.AddSeconds(30), TimeSpan.FromMinutes(1)));
        Assert.Null(cache.GetFresh(Now.AddSeconds(61), TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void Clear后不再命中()
    {
        var cache = new HostsSelectionCache();
        cache.Set(Content, 3, Now);
        Assert.NotNull(cache.GetFresh(Now));

        cache.Clear();

        Assert.Null(cache.GetFresh(Now));
        Assert.Null(cache.Peek());
    }

    [Fact]
    public void 覆盖写入以最新一次为准()
    {
        var cache = new HostsSelectionCache();
        cache.Set("1.1.1.1 github.com\n", 2, Now);
        cache.Set(Content, 3, Now.AddMinutes(2));

        var snapshot = cache.GetFresh(Now.AddMinutes(3));

        Assert.NotNull(snapshot);
        Assert.Equal(Content, snapshot!.Content);
        Assert.Equal(3, snapshot.SourceCount);
        Assert.Equal(Now.AddMinutes(2), snapshot.SelectedAt);
    }

    [Fact]
    public async Task 并发读写不抛异常且读到一致快照()
    {
        var cache = new HostsSelectionCache();
        cache.Set(Content, 3, Now);

        var readers = Task.WhenAll(Enumerable.Range(0, 64).Select(_ => Task.Run(() =>
        {
            var snapshot = cache.GetFresh(Now.AddMinutes(1));
            Assert.NotNull(snapshot);
            Assert.Equal(Content, snapshot!.Content);
        })));

        var writers = Task.WhenAll(Enumerable.Range(0, 64).Select(i => Task.Run(() =>
            cache.Set($"{i}.{i}.{i}.{i} github.com\n", 3, Now))));

        await Task.WhenAll(readers, writers);

        Assert.NotNull(cache.GetFresh(Now));
    }
}
