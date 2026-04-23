using System;
using System.IO;
using System.Linq;
using System.Threading;
using GithubAccelerator.UI.Services;

namespace GithubAccelerator.Tests;

public class OperationHistoryServiceTests : IDisposable
{
    private readonly OperationHistoryService _service;

    public OperationHistoryServiceTests()
    {
        _service = OperationHistoryService.Instance;
        _service.Clear();
    }

    public void Dispose()
    {
        _service.Clear();
    }

    [Fact]
    public void Record_AddsRecordToHistory()
    {
        var record = _service.Record(OperationType.HostsApplied, "Test operation");

        Assert.Single(_service.Records);
        Assert.Equal(OperationType.HostsApplied, record.Type);
        Assert.Equal("Test operation", record.Description);
        Assert.True(record.IsSuccess);
        Assert.Null(record.Detail);
    }

    [Fact]
    public void Record_WithFailure_SetsIsSuccessFalse()
    {
        var record = _service.Record(OperationType.HostsApplied, "Failed operation", false, "Error detail");

        Assert.False(record.IsSuccess);
        Assert.Equal("Error detail", record.Detail);
    }

    [Fact]
    public void Record_MultipleRecords_OrderedByNewestFirst()
    {
        _service.Record(OperationType.HostsApplied, "First");
        Thread.Sleep(10);
        _service.Record(OperationType.HostsViewed, "Second");
        Thread.Sleep(10);
        _service.Record(OperationType.MonitoringStarted, "Third");

        Assert.Equal(3, _service.Records.Count);
        Assert.Equal(OperationType.MonitoringStarted, _service.Records[0].Type);
        Assert.Equal(OperationType.HostsViewed, _service.Records[1].Type);
        Assert.Equal(OperationType.HostsApplied, _service.Records[2].Type);
    }

    [Fact]
    public void GetRecentRecords_ReturnsCorrectCount()
    {
        for (int i = 0; i < 10; i++)
        {
            _service.Record(OperationType.HostsApplied, $"Record {i}");
        }

        var recent = _service.GetRecentRecords(5);
        Assert.Equal(5, recent.Count);
    }

    [Fact]
    public void GetRecentRecords_MoreThanAvailable_ReturnsAll()
    {
        for (int i = 0; i < 3; i++)
        {
            _service.Record(OperationType.HostsApplied, $"Record {i}");
        }

        var recent = _service.GetRecentRecords(10);
        Assert.Equal(3, recent.Count);
    }

    [Fact]
    public void GetRecordsByType_FiltersCorrectly()
    {
        _service.Record(OperationType.HostsApplied, "Apply 1");
        _service.Record(OperationType.HostsViewed, "View 1");
        _service.Record(OperationType.HostsApplied, "Apply 2");
        _service.Record(OperationType.MonitoringStarted, "Start 1");

        var appliedRecords = _service.GetRecordsByType(OperationType.HostsApplied);
        Assert.Equal(2, appliedRecords.Count);
        Assert.All(appliedRecords, r => Assert.Equal(OperationType.HostsApplied, r.Type));
    }

    [Fact]
    public void GetRecordsByDateRange_FiltersCorrectly()
    {
        _service.Record(OperationType.HostsApplied, "Old");

        var start = DateTime.Now.AddSeconds(1);
        Thread.Sleep(1100);
        _service.Record(OperationType.HostsViewed, "New");

        var filtered = _service.GetRecordsByDateRange(start, DateTime.Now);
        Assert.Single(filtered);
        Assert.Equal("New", filtered[0].Description);
    }

    [Fact]
    public void Clear_RemovesAllRecords()
    {
        _service.Record(OperationType.HostsApplied, "Test");
        _service.Record(OperationType.HostsViewed, "Test2");

        Assert.Equal(2, _service.Records.Count);
        _service.Clear();
        Assert.Empty(_service.Records);
    }

    [Fact]
    public void Record_FiresOnOperationRecordedEvent()
    {
        OperationRecord? recordedRecord = null;
        _service.OnOperationRecorded += r => recordedRecord = r;

        var record = _service.Record(OperationType.ThemeChanged, "Theme toggled");

        Assert.NotNull(recordedRecord);
        Assert.Equal(record.Type, recordedRecord.Type);
        Assert.Equal(record.Description, recordedRecord.Description);
    }

    [Fact]
    public void TypeText_ReturnsCorrectText()
    {
        Assert.Equal("应用 Hosts", new OperationRecord { Type = OperationType.HostsApplied }.TypeText);
        Assert.Equal("查看 Hosts", new OperationRecord { Type = OperationType.HostsViewed }.TypeText);
        Assert.Equal("启动监控", new OperationRecord { Type = OperationType.MonitoringStarted }.TypeText);
        Assert.Equal("停止监控", new OperationRecord { Type = OperationType.MonitoringStopped }.TypeText);
        Assert.Equal("切换主题", new OperationRecord { Type = OperationType.ThemeChanged }.TypeText);
        Assert.Equal("创建备份", new OperationRecord { Type = OperationType.HostsBackupCreated }.TypeText);
        Assert.Equal("恢复备份", new OperationRecord { Type = OperationType.HostsBackupRestored }.TypeText);
        Assert.Equal("刷新数据源", new OperationRecord { Type = OperationType.SourcesRefreshed }.TypeText);
    }

    [Fact]
    public void StatusText_ReturnsCorrectText()
    {
        Assert.Equal("✅ 成功", new OperationRecord { IsSuccess = true }.StatusText);
        Assert.Equal("❌ 失败", new OperationRecord { IsSuccess = false }.StatusText);
    }

    [Fact]
    public void Record_PersistsToFile()
    {
        _service.Record(OperationType.HostsApplied, "Persistent record");
        _service.Record(OperationType.HostsViewed, "Another record");

        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GithubAccelerator",
            "operation_history.json");
        Assert.True(File.Exists(appDataPath));

        var json = File.ReadAllText(appDataPath);
        Assert.Contains("Persistent record", json);
        Assert.Contains("Another record", json);
    }
}

public class NotificationServiceTests
{
    private readonly NotificationService _service;

    public NotificationServiceTests()
    {
        _service = NotificationService.Instance;
        _service.NotificationsEnabled = true;
    }

    [Fact]
    public void Notify_WhenEnabled_FiresEvent()
    {
        NotificationMessage? received = null;
        _service.OnNotification += n => received = n;

        _service.Notify("Test", "Message", NotificationType.Info);

        Assert.NotNull(received);
        Assert.Equal("Test", received.Title);
        Assert.Equal("Message", received.Message);
        Assert.Equal(NotificationType.Info, received.Type);
    }

    [Fact]
    public void Notify_WhenDisabled_DoesNotFireEvent()
    {
        _service.NotificationsEnabled = false;
        NotificationMessage? received = null;
        _service.OnNotification += n => received = n;

        _service.Notify("Test", "Message");

        Assert.Null(received);
        _service.NotificationsEnabled = true;
    }

    [Fact]
    public void Info_SendsInfoNotification()
    {
        NotificationMessage? received = null;
        _service.OnNotification += n => received = n;

        _service.Info("Title", "Info message");

        Assert.NotNull(received);
        Assert.Equal(NotificationType.Info, received.Type);
    }

    [Fact]
    public void Success_SendsSuccessNotification()
    {
        NotificationMessage? received = null;
        _service.OnNotification += n => received = n;

        _service.Success("Title", "Success message");

        Assert.NotNull(received);
        Assert.Equal(NotificationType.Success, received.Type);
    }

    [Fact]
    public void Warning_SendsWarningNotification()
    {
        NotificationMessage? received = null;
        _service.OnNotification += n => received = n;

        _service.Warning("Title", "Warning message");

        Assert.NotNull(received);
        Assert.Equal(NotificationType.Warning, received.Type);
    }

    [Fact]
    public void Error_SendsErrorNotification()
    {
        NotificationMessage? received = null;
        _service.OnNotification += n => received = n;

        _service.Error("Title", "Error message");

        Assert.NotNull(received);
        Assert.Equal(NotificationType.Error, received.Type);
    }

    [Fact]
    public void TypeIcon_ReturnsCorrectIcon()
    {
        Assert.Equal("ℹ️", new NotificationMessage { Type = NotificationType.Info }.TypeIcon);
        Assert.Equal("✅", new NotificationMessage { Type = NotificationType.Success }.TypeIcon);
        Assert.Equal("⚠️", new NotificationMessage { Type = NotificationType.Warning }.TypeIcon);
        Assert.Equal("❌", new NotificationMessage { Type = NotificationType.Error }.TypeIcon);
    }
}

public class ThemeManagerTests
{
    [Fact]
    public void IsDarkMode_DefaultIsLight()
    {
        Assert.False(ThemeManager.IsDarkMode);
    }

    [Fact]
    public void CurrentTheme_DefaultIsLight()
    {
        Assert.Equal(Avalonia.Styling.ThemeVariant.Light, ThemeManager.CurrentTheme);
    }

    [Fact]
    public void ToggleTheme_ChangesTheme()
    {
        var before = ThemeManager.IsDarkMode;
        ThemeManager.ToggleTheme();
        var after = ThemeManager.IsDarkMode;

        Assert.NotEqual(before, after);

        ThemeManager.ToggleTheme();
    }
}

public class HostsEntryTests
{
    [Fact]
    public void Parse_ValidLine_ReturnsEntry()
    {
        var entry = HostsEntry.Parse("140.82.114.4 github.com");
        Assert.NotNull(entry);
        Assert.Equal("140.82.114.4", entry.Ip);
        Assert.Equal("github.com", entry.Domain);
        Assert.True(entry.IsEnabled);
    }

    [Fact]
    public void Parse_CommentedLine_ReturnsDisabledEntry()
    {
        var entry = HostsEntry.Parse("# 140.82.114.4 github.com");
        Assert.NotNull(entry);
        Assert.Equal("140.82.114.4", entry.Ip);
        Assert.Equal("github.com", entry.Domain);
        Assert.False(entry.IsEnabled);
    }

    [Fact]
    public void Parse_TabSeparated_ReturnsEntry()
    {
        var entry = HostsEntry.Parse("140.82.114.4\tgithub.com");
        Assert.NotNull(entry);
        Assert.Equal("140.82.114.4", entry.Ip);
        Assert.Equal("github.com", entry.Domain);
    }

    [Fact]
    public void Parse_EmptyLine_ReturnsNull()
    {
        Assert.Null(HostsEntry.Parse(""));
        Assert.Null(HostsEntry.Parse("   "));
        Assert.Null(HostsEntry.Parse(null!));
    }

    [Fact]
    public void Parse_CommentOnlyLine_ReturnsNull()
    {
        Assert.Null(HostsEntry.Parse("# This is a comment"));
    }

    [Fact]
    public void Parse_InvalidIp_ReturnsNull()
    {
        Assert.Null(HostsEntry.Parse("not.an.ip github.com"));
    }

    [Fact]
    public void Parse_MissingDomain_ReturnsNull()
    {
        Assert.Null(HostsEntry.Parse("140.82.114.4"));
    }

    [Fact]
    public void ToHostsLine_Enabled_ReturnsCorrectFormat()
    {
        var entry = new HostsEntry { Ip = "140.82.114.4", Domain = "github.com", IsEnabled = true };
        Assert.Equal("140.82.114.4\tgithub.com", entry.ToHostsLine());
    }

    [Fact]
    public void ToHostsLine_Disabled_ReturnsCommentedFormat()
    {
        var entry = new HostsEntry { Ip = "140.82.114.4", Domain = "github.com", IsEnabled = false };
        Assert.Equal("# 140.82.114.4\tgithub.com", entry.ToHostsLine());
    }

    [Fact]
    public void ToHostsLine_WithComment_IncludesComment()
    {
        var entry = new HostsEntry { Ip = "140.82.114.4", Domain = "github.com", IsEnabled = true, Comment = "test" };
        Assert.Contains("# test", entry.ToHostsLine());
    }
}

public class HostsGroupTests
{
    [Fact]
    public void ToHostsBlock_ContainsStartAndEndMarkers()
    {
        var group = new HostsGroup { Name = "TestGroup" };
        group.Entries.Add(new HostsEntry { Ip = "140.82.114.4", Domain = "github.com" });

        var block = group.ToHostsBlock();
        Assert.Contains("# Group:TestGroup Start", block);
        Assert.Contains("# Group:TestGroup End", block);
        Assert.Contains("140.82.114.4\tgithub.com", block);
    }

    [Fact]
    public void EnabledCount_ReturnsCorrectCount()
    {
        var group = new HostsGroup();
        group.Entries.Add(new HostsEntry { Ip = "1.1.1.1", Domain = "a.com", IsEnabled = true });
        group.Entries.Add(new HostsEntry { Ip = "2.2.2.2", Domain = "b.com", IsEnabled = false });
        group.Entries.Add(new HostsEntry { Ip = "3.3.3.3", Domain = "c.com", IsEnabled = true });

        Assert.Equal(2, group.EnabledCount);
        Assert.Equal(3, group.TotalCount);
    }
}

public class HostsGroupServiceTests
{
    [Fact]
    public void CreateGroup_ReturnsValidGroup()
    {
        var service = HostsGroupService.Instance;
        var group = service.CreateGroup($"CreateTest_{Guid.NewGuid():N}", "Test description");

        Assert.NotNull(group);
        Assert.NotEmpty(group.Id);
        Assert.True(group.IsEnabled);
        Assert.NotEmpty(group.Name);
        Assert.Equal("Test description", group.Description);

        var found = service.GetGroup(group.Id);
        Assert.NotNull(found);
        Assert.Equal(group.Id, found.Id);
    }

    [Fact]
    public void DeleteGroup_RemovesSpecificGroup()
    {
        var service = HostsGroupService.Instance;
        var group = service.CreateGroup($"DeleteTest_{Guid.NewGuid():N}");

        var found = service.GetGroup(group.Id);
        Assert.NotNull(found);

        service.DeleteGroup(group.Id);

        var afterDelete = service.GetGroup(group.Id);
        Assert.Null(afterDelete);
    }

    [Fact]
    public void UpdateGroup_UpdatesProperties()
    {
        var service = HostsGroupService.Instance;
        var group = service.CreateGroup($"UpdateTest_{Guid.NewGuid():N}", "Original desc", "#FF0000");

        service.UpdateGroup(group.Id, name: "Updated", description: "Updated desc", color: "#00FF00", isEnabled: false);

        var updated = service.GetGroup(group.Id);
        Assert.NotNull(updated);
        Assert.Equal("Updated", updated.Name);
        Assert.Equal("Updated desc", updated.Description);
        Assert.Equal("#00FF00", updated.Color);
        Assert.False(updated.IsEnabled);
    }

    [Fact]
    public void AddEntry_AddsToGroup()
    {
        var service = HostsGroupService.Instance;
        var group = service.CreateGroup($"AddEntryTest_{Guid.NewGuid():N}");
        var entry = new HostsEntry { Ip = "140.82.114.4", Domain = "github.com" };

        service.AddEntry(group.Id, entry);

        var updated = service.GetGroup(group.Id);
        Assert.NotNull(updated);
        Assert.Contains(updated.Entries, e => e.Domain == "github.com" && e.Ip == "140.82.114.4");
    }

    [Fact]
    public void RemoveEntry_RemovesFromGroup()
    {
        var service = HostsGroupService.Instance;
        var group = service.CreateGroup($"RemoveEntryTest_{Guid.NewGuid():N}");
        service.AddEntry(group.Id, new HostsEntry { Ip = "140.82.114.4", Domain = "github.com" });
        service.AddEntry(group.Id, new HostsEntry { Ip = "140.82.113.6", Domain = "api.github.com" });

        service.RemoveEntry(group.Id, "github.com");

        var updated = service.GetGroup(group.Id);
        Assert.NotNull(updated);
        Assert.DoesNotContain(updated.Entries, e => e.Domain == "github.com");
        Assert.Contains(updated.Entries, e => e.Domain == "api.github.com");
    }

    [Fact]
    public void ToggleEntry_ChangesEnabledState()
    {
        var service = HostsGroupService.Instance;
        var group = service.CreateGroup($"ToggleTest_{Guid.NewGuid():N}");
        service.AddEntry(group.Id, new HostsEntry { Ip = "140.82.114.4", Domain = "github.com", IsEnabled = true });

        service.ToggleEntry(group.Id, "github.com", false);

        var updated = service.GetGroup(group.Id);
        Assert.NotNull(updated);
        var entry = updated.Entries.FirstOrDefault(e => e.Domain == "github.com");
        Assert.NotNull(entry);
        Assert.False(entry.IsEnabled);
    }

    [Fact]
    public void GenerateHostsContent_OnlyIncludesEnabledGroups()
    {
        var service = HostsGroupService.Instance;
        var uniqueDomain1 = $"enabled_{Guid.NewGuid():N}.test";
        var uniqueDomain2 = $"disabled_{Guid.NewGuid():N}.test";

        var group1 = service.CreateGroup($"EnabledGroup_{Guid.NewGuid():N}");
        group1.IsEnabled = true;
        service.AddEntry(group1.Id, new HostsEntry { Ip = "1.1.1.1", Domain = uniqueDomain1 });

        var group2 = service.CreateGroup($"DisabledGroup_{Guid.NewGuid():N}");
        group2.IsEnabled = false;
        service.AddEntry(group2.Id, new HostsEntry { Ip = "2.2.2.2", Domain = uniqueDomain2 });

        var content = service.GenerateHostsContent();
        Assert.Contains(uniqueDomain1, content);
        Assert.DoesNotContain(uniqueDomain2, content);
    }

    [Fact]
    public void ImportFromHostsContent_CreatesGroupWithEntries()
    {
        var service = HostsGroupService.Instance;
        var uniqueName = $"Imported_{Guid.NewGuid():N}";
        var hostsContent = @"140.82.114.4 github.com
140.82.113.6 api.github.com
# This is a comment
199.232.69.194 raw.githubusercontent.com";

        service.ImportFromHostsContent(hostsContent, uniqueName);

        var imported = service.Groups.FirstOrDefault(g => g.Name == uniqueName);
        Assert.NotNull(imported);
        Assert.Equal(3, imported.Entries.Count);
    }

    [Fact]
    public void GetGroup_WithInvalidId_ReturnsNull()
    {
        Assert.Null(HostsGroupService.Instance.GetGroup("nonexistent_id"));
    }

    [Fact]
    public void DeleteGroup_WithInvalidId_ReturnsFalse()
    {
        Assert.False(HostsGroupService.Instance.DeleteGroup("nonexistent_id"));
    }

    [Fact]
    public void OnGroupsChanged_FiredOnCreate()
    {
        var service = HostsGroupService.Instance;
        bool eventFired = false;
        service.OnGroupsChanged += () => eventFired = true;

        service.CreateGroup($"EventTest_{Guid.NewGuid():N}");
        Assert.True(eventFired);
    }

    [Fact]
    public void Clear_RemovesAllGroups()
    {
        var service = HostsGroupService.Instance;
        service.CreateGroup($"ClearTest1_{Guid.NewGuid():N}");
        service.CreateGroup($"ClearTest2_{Guid.NewGuid():N}");

        service.Clear();
        Assert.Empty(service.Groups);
    }
}

public class DataExportImportServiceTests : IDisposable
{
    private readonly string _testDir;

    public DataExportImportServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"GA_Test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }
        catch { }
    }

    [Fact]
    public async Task ExportAsync_CreatesZipFile()
    {
        var groupService = HostsGroupService.Instance;
        var group = groupService.CreateGroup($"ExportTest_{Guid.NewGuid():N}");
        groupService.AddEntry(group.Id, new HostsEntry { Ip = "140.82.114.4", Domain = "github.com" });

        var filePath = Path.Combine(_testDir, "export.zip");
        var result = await DataExportImportService.Instance.ExportAsync(filePath);

        Assert.True(result);
        Assert.True(File.Exists(filePath));
    }

    [Fact]
    public async Task ExportAsync_CreatesJsonFile()
    {
        var groupService = HostsGroupService.Instance;
        groupService.CreateGroup($"JsonExportTest_{Guid.NewGuid():N}");

        var filePath = Path.Combine(_testDir, "export.json");
        var result = await DataExportImportService.Instance.ExportAsync(filePath);

        Assert.True(result);
        Assert.True(File.Exists(filePath));
    }

    [Fact]
    public async Task ImportAsync_FromZip_ReturnsData()
    {
        var uniqueName = $"ImportTest_{Guid.NewGuid():N}";
        var groupService = HostsGroupService.Instance;
        var group = groupService.CreateGroup(uniqueName);
        groupService.AddEntry(group.Id, new HostsEntry { Ip = "1.1.1.1", Domain = "test.com" });

        var exportPath = Path.Combine(_testDir, "export.zip");
        await DataExportImportService.Instance.ExportAsync(exportPath);

        var data = await DataExportImportService.Instance.ImportAsync(exportPath);
        Assert.NotNull(data);
        Assert.Equal("1.0", data.Version);
        Assert.Contains(data.Groups, g => g.Name == uniqueName);
    }

    [Fact]
    public async Task ImportAsync_NonexistentFile_ReturnsNull()
    {
        var data = await DataExportImportService.Instance.ImportAsync(Path.Combine(_testDir, "nonexistent.zip"));
        Assert.Null(data);
    }

    [Fact]
    public async Task ImportAsync_InvalidFile_ReturnsNull()
    {
        var invalidPath = Path.Combine(_testDir, "invalid.zip");
        await File.WriteAllTextAsync(invalidPath, "this is not a valid zip or json");
        var data = await DataExportImportService.Instance.ImportAsync(invalidPath);
        Assert.Null(data);
    }

    [Fact]
    public async Task ApplyImportedDataAsync_ImportsGroups()
    {
        var data = new ExportData
        {
            Groups = new List<HostsGroup>
            {
                new()
                {
                    Name = $"AppliedGroup_{Guid.NewGuid():N}",
                    Description = "Test",
                    Entries = new List<HostsEntry>
                    {
                        new() { Ip = "2.2.2.2", Domain = "applied.com", IsEnabled = true }
                    }
                }
            }
        };

        var result = await DataExportImportService.Instance.ApplyImportedDataAsync(data);
        Assert.True(result);
    }

    [Fact]
    public async Task ExportAsync_FiresProgressEvents()
    {
        var groupService = HostsGroupService.Instance;
        groupService.CreateGroup($"ProgressTest_{Guid.NewGuid():N}");

        var filePath = Path.Combine(_testDir, "progress.zip");
        var messages = new List<string>();
        var service = DataExportImportService.Instance;
        service.OnExportProgress += msg => messages.Add(msg);

        await service.ExportAsync(filePath);

        Assert.NotEmpty(messages);
        Assert.Contains("导出完成", messages[^1]);
    }

    [Fact]
    public async Task ImportAsync_FiresProgressEvents()
    {
        var groupService = HostsGroupService.Instance;
        groupService.CreateGroup($"ProgressImportTest_{Guid.NewGuid():N}");

        var exportPath = Path.Combine(_testDir, "progress_import.zip");
        await DataExportImportService.Instance.ExportAsync(exportPath);

        var messages = new List<string>();
        var service = DataExportImportService.Instance;
        service.OnImportProgress += msg => messages.Add(msg);

        await service.ImportAsync(exportPath);

        Assert.NotEmpty(messages);
    }

    [Fact]
    public async Task ExportImport_Roundtrip_PreservesData()
    {
        var uniqueName = $"RT_{Guid.NewGuid():N}";
        var uniqueDomain1 = $"rt_{Guid.NewGuid():N}.com";
        var uniqueDomain2 = $"rt_{Guid.NewGuid():N}.com";

        var groupService = HostsGroupService.Instance;
        var group = groupService.CreateGroup(uniqueName, "Roundtrip desc", "#FF0000");
        groupService.AddEntry(group.Id, new HostsEntry { Ip = "3.3.3.3", Domain = uniqueDomain1, IsEnabled = true, Comment = "test comment" });
        groupService.AddEntry(group.Id, new HostsEntry { Ip = "4.4.4.4", Domain = uniqueDomain2, IsEnabled = false });

        var exportPath = Path.Combine(_testDir, $"roundtrip_{Guid.NewGuid():N}.zip");
        var exportResult = await DataExportImportService.Instance.ExportAsync(exportPath);
        Assert.True(exportResult, "导出应该成功");
        Assert.True(File.Exists(exportPath), "导出文件应该存在");

        await Task.Delay(100);

        var data = await DataExportImportService.Instance.ImportAsync(exportPath);

        Assert.NotNull(data);
        Assert.NotEmpty(data.Groups);
        var importedGroup = data.Groups.FirstOrDefault(g => g.Name == uniqueName);
        Assert.NotNull(importedGroup);
        Assert.Equal("Roundtrip desc", importedGroup.Description);
        Assert.Equal("#FF0000", importedGroup.Color);
        Assert.Equal(2, importedGroup.Entries.Count);
        Assert.Contains(importedGroup.Entries, e => e.Domain == uniqueDomain1);
        Assert.Contains(importedGroup.Entries, e => e.Domain == uniqueDomain2);
    }
}

public class ChartDataPointTests
{
    [Fact]
    public void ChartDataPoint_DefaultValues_AreValid()
    {
        var point = new ChartDataPoint();
        Assert.Equal(default(DateTime), point.Time);
        Assert.Equal(0, point.Value);
        Assert.Equal(string.Empty, point.Label);
    }

    [Fact]
    public void ChartDataPoint_CanSetProperties()
    {
        var point = new ChartDataPoint
        {
            Time = DateTime.Now,
            Value = 100.5,
            Label = "Test Point"
        };

        Assert.Equal(100.5, point.Value);
        Assert.Equal("Test Point", point.Label);
    }
}

public class ChartSeriesTests
{
    [Fact]
    public void ChartSeries_DefaultValues_AreValid()
    {
        var series = new ChartSeries();
        Assert.Equal(string.Empty, series.Name);
        Assert.Equal("#2196F3", series.Color);
        Assert.NotNull(series.DataPoints);
        Assert.Empty(series.DataPoints);
    }

    [Fact]
    public void ChartSeries_CanAddDataPoints()
    {
        var series = new ChartSeries { Name = "Test Series", Color = "#FF0000" };
        series.DataPoints.Add(new ChartDataPoint { Value = 10 });
        series.DataPoints.Add(new ChartDataPoint { Value = 20 });

        Assert.Equal("Test Series", series.Name);
        Assert.Equal("#FF0000", series.Color);
        Assert.Equal(2, series.DataPoints.Count);
    }
}

public class PerformanceChartServiceTests
{
    [Fact]
    public void Service_IsSingleton()
    {
        var instance1 = PerformanceChartService.Instance;
        var instance2 = PerformanceChartService.Instance;
        Assert.Same(instance1, instance2);
    }

    [Fact]
    public void GetResponseTimeHistory_WithoutMonitor_ReturnsEmpty()
    {
        var service = PerformanceChartService.Instance;
        service.SetMonitor(null);
        var result = service.GetResponseTimeHistory(50);
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void GetSuccessRateComparison_WithoutMonitor_ReturnsEmpty()
    {
        var service = PerformanceChartService.Instance;
        service.SetMonitor(null);
        var result = service.GetSuccessRateComparison();
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void GetSourceSummary_WithoutMonitor_ReturnsEmpty()
    {
        var service = PerformanceChartService.Instance;
        service.SetMonitor(null);
        var summary = service.GetSourceSummary();
        Assert.NotNull(summary);
        Assert.Empty(summary);
    }

    [Fact]
    public void GetResponseTimeStats_WithoutMonitor_ReturnsZeros()
    {
        var service = PerformanceChartService.Instance;
        service.SetMonitor(null);
        var (min, max, avg) = service.GetResponseTimeStats("http://test.com");
        Assert.Equal(0, min);
        Assert.Equal(0, max);
        Assert.Equal(0, avg);
    }

    [Fact]
    public void OnDataChanged_CanBeSubscribed()
    {
        var service = PerformanceChartService.Instance;
        var fired = false;
        service.OnDataChanged += () => fired = true;

        service.RefreshData();

        Assert.True(fired);
    }

    [Fact]
    public void GetScoreDistribution_WithoutMonitor_ReturnsEmpty()
    {
        var service = PerformanceChartService.Instance;
        service.SetMonitor(null);
        var result = service.GetScoreDistribution(0);
        Assert.NotNull(result);
        Assert.Empty(result);
    }
}
