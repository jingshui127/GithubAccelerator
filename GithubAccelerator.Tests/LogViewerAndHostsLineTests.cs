using System;
using System.Linq;
using System.Threading.Tasks;
using GithubAccelerator.UI.ViewModels;

namespace GithubAccelerator.Tests;

/// <summary>
/// LogViewerViewModel 的搜索/筛选功能测试，以及 HostsLineViewModel 的行号/匹配状态测试。
/// 注意：直接操作 <see cref="LogViewerViewModel.Logs"/> 集合避免依赖 Serilog 单例初始化，
/// 通过设置 SearchText/SelectedLogLevel 触发 FilterLogs 重建 FilteredLogs。
/// </summary>
public class LogViewerAndHostsLineTests
{
    private static LogEntryViewModel CreateEntry(string level, string message, string time = "12:00:00")
    {
        var entry = new GithubAccelerator.UI.Services.LogEntry
        {
            Timestamp = DateTime.Parse($"2026-01-01 {time}"),
            Level = level,
            Message = message
        };
        return new LogEntryViewModel(entry);
    }

    [Fact]
    public void HostsLineViewModel_LineNumberText_ReturnsZeroPadded()
    {
        var line = new HostsLineViewModel
        {
            LineNumber = 42,
            Text = "127.0.0.1 github.com"
        };

        Assert.Equal("0042", line.LineNumberText);
    }

    [Fact]
    public void HostsLineViewModel_LineNumberChanged_RaisesLineNumberTextNotification()
    {
        var line = new HostsLineViewModel { LineNumber = 1 };
        string? notified = null;
        line.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(HostsLineViewModel.LineNumberText))
            {
                notified = line.LineNumberText;
            }
        };

        line.LineNumber = 99;

        Assert.Equal("0099", notified);
    }

    [Fact]
    public void HostsLineViewModel_IsMatchChanged_RaisesMatchBackgroundNotification()
    {
        var line = new HostsLineViewModel { IsMatch = false };
        bool notified = false;
        line.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(HostsLineViewModel.MatchBackground))
            {
                notified = true;
            }
        };

        line.IsMatch = true;

        Assert.True(notified);
    }

    [Fact]
    public void HostsLineViewModel_MatchBackground_ReflectsIsMatch()
    {
        var line = new HostsLineViewModel { IsMatch = false };
        var defaultBrush = line.MatchBackground;

        line.IsMatch = true;
        var matchBrush = line.MatchBackground;

        Assert.NotSame(defaultBrush, matchBrush);
    }

    [Fact]
    public void LogViewer_Search_FiltersByKeyword()
    {
        var vm = new LogViewerViewModel();
        // 清空默认可能残留的条目（LogService 单例）
        vm.Logs.Clear();

        vm.Logs.Add(CreateEntry("Information", "info message alpha"));
        vm.Logs.Add(CreateEntry("Warning", "warning message gamma"));
        vm.Logs.Add(CreateEntry("Error", "error message delta"));

        // 触发 FilterLogs
        vm.SearchText = "warning";

        Assert.Single(vm.FilteredLogs);
        Assert.Contains(vm.FilteredLogs, l => l.Level == "Warning");

        // 清空搜索应恢复全部
        vm.SearchText = string.Empty;

        Assert.Equal(3, vm.FilteredLogs.Count);
    }

    [Fact]
    public void LogViewer_Search_FiltersByLevelAndKeyword()
    {
        var vm = new LogViewerViewModel();
        vm.Logs.Clear();

        vm.Logs.Add(CreateEntry("Information", "info alpha"));
        vm.Logs.Add(CreateEntry("Information", "info beta"));
        vm.Logs.Add(CreateEntry("Warning", "warning alpha"));
        vm.Logs.Add(CreateEntry("Error", "error alpha"));

        vm.SelectedLogLevel = "Information";
        vm.SearchText = "alpha";

        // 级别=Information 且关键词=alpha → 只匹配 "info alpha"
        Assert.Single(vm.FilteredLogs);
        Assert.Equal("Information", vm.FilteredLogs.First().Level);
    }

    [Fact]
    public void LogViewer_Search_IsCaseInsensitive()
    {
        var vm = new LogViewerViewModel();
        vm.Logs.Clear();

        vm.Logs.Add(CreateEntry("Information", "GitHub Connection Successful"));

        vm.SearchText = "GITHUB";
        Assert.Single(vm.FilteredLogs);

        vm.SearchText = "connection";
        Assert.Single(vm.FilteredLogs);
    }

    [Fact]
    public void LogViewer_Search_NoMatch_HasNoLogsIsTrue()
    {
        var vm = new LogViewerViewModel();
        vm.Logs.Clear();

        vm.Logs.Add(CreateEntry("Information", "hello world"));

        vm.SearchText = "xyz_nothing_matches";

        Assert.True(vm.HasNoLogs);
    }

    [Fact]
    public async Task LogViewer_ExportLogs_NoDelegate_ReturnsSilently()
    {
        var vm = new LogViewerViewModel();
        // 不设置 SaveFileAsync，应静默返回
        await vm.ExportLogsCommand.ExecuteAsync(null);
        // 无异常即视为通过
        Assert.True(true);
    }

    [Fact]
    public void LogViewer_SetClipboardTextAsync_PropagatesToExistingEntries()
    {
        var vm = new LogViewerViewModel();
        vm.Logs.Clear();

        vm.Logs.Add(CreateEntry("Information", "test message"));

        // 初始时 SetClipboardTextAsync 为 null，子条目也应为 null
        Assert.Null(vm.SetClipboardTextAsync);
        Assert.Null(vm.Logs.First().SetClipboardTextAsync);

        // 设置后应传播到已有条目
        Func<string, Task> captured = _ => Task.CompletedTask;
        vm.SetClipboardTextAsync = captured;

        Assert.NotNull(vm.Logs.First().SetClipboardTextAsync);
        Assert.Same(captured, vm.Logs.First().SetClipboardTextAsync);
    }

    [Fact]
    public void LogViewer_Search_MatchesTimeOrLevelOrMessage()
    {
        var vm = new LogViewerViewModel();
        vm.Logs.Clear();

        vm.Logs.Add(CreateEntry("Information", "msg1", "10:00:00"));
        vm.Logs.Add(CreateEntry("Warning", "msg2", "11:00:00"));
        vm.Logs.Add(CreateEntry("Error", "msg3", "12:00:00"));

        // 匹配级别
        vm.SearchText = "Warning";
        Assert.Single(vm.FilteredLogs);

        // 匹配时间
        vm.SearchText = "11:00";
        Assert.Single(vm.FilteredLogs);

        // 匹配消息
        vm.SearchText = "msg3";
        Assert.Single(vm.FilteredLogs);
    }
}
