using Xunit;

namespace GithubAccelerator.Tests;

/// <summary>
/// 进程级单例（HostsGroupService / DataExportImportService / OperationHistoryService）是共享状态：
/// 这些测试类会 Clear 全局集合或读取全局记录，必须串行执行，不能与其它集合并行。
/// 这是测试隔离问题（并行测试互相清空数据），不是产品缺陷。
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class GlobalStateCollection
{
    public const string Name = "GlobalState";
}
