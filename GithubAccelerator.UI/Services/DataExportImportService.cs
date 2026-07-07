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
    public int TestInterval { get; set; } = 600;
    public bool AutoStart { get; set; }
    public bool AutoApplyHosts { get; set; }
    public bool AutoSwitchBestSource { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public bool StartMinimized { get; set; }
    public bool AutoFlushDns { get; set; } = true;
    public bool ShowQuickGuideOnStartup { get; set; } = true;
    public string LogLevel { get; set; } = "Information";
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

    public async Task<bool> ExportAsync(string filePath, ViewModels.SettingsViewModel? settings = null)
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
                Settings = settings != null ? new AppSettingsData
                {
                    IsDarkMode = ThemeManager.IsDarkMode,
                    TestInterval = settings.TestInterval,
                    AutoStart = settings.AutoStart,
                    AutoApplyHosts = settings.AutoApplyHosts,
                    AutoSwitchBestSource = settings.AutoSwitchBestSource,
                    MinimizeToTray = settings.MinimizeToTray,
                    StartMinimized = settings.StartMinimized,
                    AutoFlushDns = settings.AutoFlushDns,
                    ShowQuickGuideOnStartup = settings.ShowQuickGuideOnStartup,
                    LogLevel = settings.LogLevel
                } : new AppSettingsData { IsDarkMode = ThemeManager.IsDarkMode }
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

    public async Task<bool> ApplyImportedDataAsync(ExportData data, ViewModels.SettingsViewModel? settings = null)
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

            if (data.Settings != null && settings != null)
            {
                if (data.Settings.IsDarkMode != ThemeManager.IsDarkMode)
                {
                    ThemeManager.ToggleTheme();
                }
                
                settings.TestInterval = data.Settings.TestInterval;
                settings.AutoStart = data.Settings.AutoStart;
                settings.AutoApplyHosts = data.Settings.AutoApplyHosts;
                settings.AutoSwitchBestSource = data.Settings.AutoSwitchBestSource;
                settings.MinimizeToTray = data.Settings.MinimizeToTray;
                settings.StartMinimized = data.Settings.StartMinimized;
                settings.AutoFlushDns = data.Settings.AutoFlushDns;
                settings.ShowQuickGuideOnStartup = data.Settings.ShowQuickGuideOnStartup;
                settings.LogLevel = data.Settings.LogLevel;
                
                settings.Save();
            }

            return true;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "应用导入数据失败");
            return false;
        }
    }
}
