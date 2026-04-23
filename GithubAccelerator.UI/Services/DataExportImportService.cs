using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace GithubAccelerator.UI.Services;

public class ExportData
{
    public string Version { get; set; } = "1.0";
    public DateTime ExportTime { get; set; } = DateTime.Now;
    public List<HostsGroup> Groups { get; set; } = new();
    public List<OperationRecord> OperationHistory { get; set; } = new();
    public AppSettingsData Settings { get; set; } = new();
}

public class AppSettingsData
{
    public bool IsDarkMode { get; set; }
    public bool AutoStartMonitoring { get; set; }
    public int MonitoringIntervalSeconds { get; set; } = 30;
    public bool MinimizeToTray { get; set; }
    public bool AutoUpdateHosts { get; set; }
    public int AutoUpdateIntervalMinutes { get; set; } = 120;
}

public class DataExportImportService
{
    private static readonly Lazy<DataExportImportService> _instance = new(() => new DataExportImportService());
    public static DataExportImportService Instance => _instance.Value;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public event Action<string>? OnExportProgress;
    public event Action<string>? OnImportProgress;

    public async Task<bool> ExportAsync(string filePath)
    {
        try
        {
            OnExportProgress?.Invoke("正在收集数据...");

            var data = new ExportData
            {
                Version = "1.0",
                ExportTime = DateTime.Now,
                Groups = new List<HostsGroup>(HostsGroupService.Instance.Groups),
                OperationHistory = new List<OperationRecord>(OperationHistoryService.Instance.Records),
                Settings = new AppSettingsData
                {
                    IsDarkMode = ThemeManager.IsDarkMode,
                    AutoStartMonitoring = false,
                    MonitoringIntervalSeconds = 30,
                    MinimizeToTray = false,
                    AutoUpdateHosts = false,
                    AutoUpdateIntervalMinutes = 120
                }
            };

            OnExportProgress?.Invoke("正在序列化数据...");
            var json = JsonSerializer.Serialize(data, _jsonOptions);

            OnExportProgress?.Invoke("正在压缩数据...");
            var tempPath = Path.GetTempFileName();
            try
            {
                await File.WriteAllTextAsync(tempPath, json);

                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }

                using var fs = new FileStream(filePath, FileMode.Create);
                using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
                var entry = zip.CreateEntry("data.json", CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                using var fileStream = File.OpenRead(tempPath);
                await fileStream.CopyToAsync(entryStream);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }

            OnExportProgress?.Invoke("导出完成！");
            return true;
        }
        catch (Exception ex)
        {
            OnExportProgress?.Invoke($"导出失败：{ex.Message}");
            return false;
        }
    }

    public async Task<ExportData?> ImportAsync(string filePath)
    {
        try
        {
            OnImportProgress?.Invoke("正在读取文件...");

            if (!File.Exists(filePath))
            {
                OnImportProgress?.Invoke("文件不存在");
                return null;
            }

            var tempPath = Path.GetTempFileName();
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open);
                using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
                var entry = zip.GetEntry("data.json");
                if (entry == null)
                {
                    OnImportProgress?.Invoke("无效的导出文件格式");
                    return null;
                }

                using var entryStream = entry.Open();
                using var fileStream = File.Create(tempPath);
                await entryStream.CopyToAsync(fileStream);
                await fileStream.FlushAsync();
            }
            catch (InvalidDataException)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(filePath);
                    var data = JsonSerializer.Deserialize<ExportData>(json, _jsonOptions);
                    OnImportProgress?.Invoke("导入完成！");
                    return data;
                }
                catch
                {
                    OnImportProgress?.Invoke("无效的文件格式");
                    return null;
                }
            }

            OnImportProgress?.Invoke("正在解析数据...");
            var content = await File.ReadAllTextAsync(tempPath);
            var exportData = JsonSerializer.Deserialize<ExportData>(content, _jsonOptions);

            OnImportProgress?.Invoke("导入完成！");
            return exportData;
        }
        catch (Exception ex)
        {
            OnImportProgress?.Invoke($"导入失败：{ex.Message}");
            return null;
        }
        finally
        {
        }
    }

    public async Task<bool> ApplyImportedDataAsync(ExportData data)
    {
        try
        {
            if (data.Groups?.Count > 0)
            {
                var groupService = HostsGroupService.Instance;
                foreach (var group in data.Groups)
                {
                    group.Id = Guid.NewGuid().ToString("N")[..8];
                    group.CreatedAt = DateTime.Now;
                    group.UpdatedAt = DateTime.Now;
                    groupService.CreateGroup(group.Name, group.Description, group.Color);
                    var newGroup = groupService.Groups[^1];
                    foreach (var entry in group.Entries)
                    {
                        groupService.AddEntry(newGroup.Id, new HostsEntry
                        {
                            Ip = entry.Ip,
                            Domain = entry.Domain,
                            IsEnabled = entry.IsEnabled,
                            Comment = entry.Comment
                        });
                    }
                }
            }

            if (data.Settings != null)
            {
                if (data.Settings.IsDarkMode != ThemeManager.IsDarkMode)
                {
                    ThemeManager.ToggleTheme();
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
