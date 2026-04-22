using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GithubAccelerator.UI.Services;

public enum OperationType
{
    HostsApplied,
    HostsRestored,
    HostsBackupCreated,
    HostsBackupRestored,
    HostsViewed,
    HostsOpenedInNotepad,
    MonitoringStarted,
    MonitoringStopped,
    SourcesRefreshed,
    ThemeChanged,
    SettingsChanged
}

public class OperationRecord
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public OperationType Type { get; set; }
    public string Description { get; set; } = string.Empty;
    public bool IsSuccess { get; set; } = true;
    public string? Detail { get; set; }

    public string TypeText => Type switch
    {
        OperationType.HostsApplied => "应用 Hosts",
        OperationType.HostsRestored => "恢复 Hosts",
        OperationType.HostsBackupCreated => "创建备份",
        OperationType.HostsBackupRestored => "恢复备份",
        OperationType.HostsViewed => "查看 Hosts",
        OperationType.HostsOpenedInNotepad => "记事本打开",
        OperationType.MonitoringStarted => "启动监控",
        OperationType.MonitoringStopped => "停止监控",
        OperationType.SourcesRefreshed => "刷新数据源",
        OperationType.ThemeChanged => "切换主题",
        OperationType.SettingsChanged => "修改设置",
        _ => Type.ToString()
    };

    public string StatusText => IsSuccess ? "✅ 成功" : "❌ 失败";
}

public class OperationHistoryService
{
    private static readonly Lazy<OperationHistoryService> _instance = new(() => new OperationHistoryService());
    public static OperationHistoryService Instance => _instance.Value;

    private readonly List<OperationRecord> _records = new();
    private readonly string _historyFilePath;
    private const int MaxRecords = 500;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public IReadOnlyList<OperationRecord> Records => _records.AsReadOnly();

    public event Action<OperationRecord>? OnOperationRecorded;

    private OperationHistoryService()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GithubAccelerator");
        Directory.CreateDirectory(appDataPath);
        _historyFilePath = Path.Combine(appDataPath, "operation_history.json");
        LoadFromFile();
    }

    public OperationRecord Record(OperationType type, string description, bool isSuccess = true, string? detail = null)
    {
        var record = new OperationRecord
        {
            Timestamp = DateTime.Now,
            Type = type,
            Description = description,
            IsSuccess = isSuccess,
            Detail = detail
        };

        _records.Insert(0, record);

        if (_records.Count > MaxRecords)
        {
            _records.RemoveAt(_records.Count - 1);
        }

        SaveToFile();
        OnOperationRecorded?.Invoke(record);
        return record;
    }

    public void Clear()
    {
        _records.Clear();
        SaveToFile();
    }

    public IReadOnlyList<OperationRecord> GetRecentRecords(int count = 50)
    {
        return _records.Count <= count ? _records.AsReadOnly() : _records.GetRange(0, count).AsReadOnly();
    }

    public IReadOnlyList<OperationRecord> GetRecordsByType(OperationType type)
    {
        return _records.FindAll(r => r.Type == type).AsReadOnly();
    }

    public IReadOnlyList<OperationRecord> GetRecordsByDateRange(DateTime start, DateTime end)
    {
        return _records.FindAll(r => r.Timestamp >= start && r.Timestamp <= end).AsReadOnly();
    }

    private void SaveToFile()
    {
        try
        {
            var json = JsonSerializer.Serialize(_records, _jsonOptions);
            File.WriteAllText(_historyFilePath, json);
        }
        catch
        {
        }
    }

    private void LoadFromFile()
    {
        try
        {
            if (!File.Exists(_historyFilePath)) return;
            var json = File.ReadAllText(_historyFilePath);
            var records = JsonSerializer.Deserialize<List<OperationRecord>>(json, _jsonOptions);
            if (records != null)
            {
                _records.Clear();
                _records.AddRange(records);
            }
        }
        catch
        {
        }
    }
}
