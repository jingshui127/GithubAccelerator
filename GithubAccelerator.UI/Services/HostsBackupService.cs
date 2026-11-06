using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using GithubAccelerator.Services;

namespace GithubAccelerator.UI.Services;

public class HostsBackup
{
    public DateTime BackupTime { get; set; }
    public string BackupPath { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? Note { get; set; }
}

public interface IHostsBackupService
{
    Task<HostsBackup> CreateBackupAsync(string? note = null);

    /// <summary>
    /// 首次加速前创建一份"完整快照"（固定文件名，不参与轮转清理）；已存在则直接返回已有快照。
    /// </summary>
    Task<HostsBackup?> EnsureFirstRunSnapshotAsync();

    /// <summary>读取"首启前完整快照"，不存在返回 null。</summary>
    Task<HostsBackup?> GetFirstRunSnapshotAsync();

    Task<List<HostsBackup>> GetAllBackupsAsync();
    Task<bool> RestoreBackupAsync(string backupPath);
    Task<bool> DeleteBackupAsync(string backupPath);
    void CleanupOldBackups(int keepCount = 5);
}

public class HostsBackupService : IHostsBackupService
{
    private const string FirstRunSnapshotFileName = "hosts_first_run_snapshot.txt";

    private readonly string _backupDirectory;
    private readonly IHostsFileService _hostsFileService;

    public HostsBackupService(IHostsFileService hostsFileService)
    {
        _hostsFileService = hostsFileService;
        _backupDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GithubAccelerator",
            "HostsBackups");

        if (!Directory.Exists(_backupDirectory))
        {
            Directory.CreateDirectory(_backupDirectory);
        }
    }

    public async Task<HostsBackup> CreateBackupAsync(string? note = null)
    {
        var content = await _hostsFileService.ReadHostsFileAsync();
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var fileName = $"hosts_backup_{timestamp}.txt";
        var backupPath = Path.Combine(_backupDirectory, fileName);

        await File.WriteAllTextAsync(backupPath, content);

        var backup = new HostsBackup
        {
            BackupTime = DateTime.Now,
            BackupPath = backupPath,
            Content = content,
            Note = note
        };

        SaveBackupMetadata(backup);

        CleanupOldBackups(5);

        return backup;
    }

    public async Task<HostsBackup?> EnsureFirstRunSnapshotAsync()
    {
        var snapshotPath = Path.Combine(_backupDirectory, FirstRunSnapshotFileName);
        if (File.Exists(snapshotPath))
        {
            return await ReadSnapshotAsync(snapshotPath);
        }

        var content = await _hostsFileService.ReadHostsFileAsync();
        var backup = new HostsBackup
        {
            BackupTime = DateTime.Now,
            BackupPath = snapshotPath,
            Content = content,
            Note = "首启前完整快照"
        };

        await File.WriteAllTextAsync(snapshotPath, content);
        SaveBackupMetadata(backup);
        return backup;
    }

    public async Task<HostsBackup?> GetFirstRunSnapshotAsync()
    {
        var snapshotPath = Path.Combine(_backupDirectory, FirstRunSnapshotFileName);
        return File.Exists(snapshotPath) ? await ReadSnapshotAsync(snapshotPath) : null;
    }

    private static async Task<HostsBackup?> ReadSnapshotAsync(string snapshotPath)
    {
        try
        {
            var content = await File.ReadAllTextAsync(snapshotPath);
            HostsBackup? backup = null;
            var metadataPath = snapshotPath + ".meta";
            if (File.Exists(metadataPath))
            {
                backup = JsonSerializer.Deserialize<HostsBackup>(await File.ReadAllTextAsync(metadataPath));
            }

            backup ??= new HostsBackup { BackupTime = File.GetLastWriteTime(snapshotPath) };
            backup.BackupPath = snapshotPath;
            backup.Content = content;
            return backup;
        }
        catch
        {
            return null;
        }
    }

    public async Task<List<HostsBackup>> GetAllBackupsAsync()
    {
        var backups = new List<HostsBackup>();

        if (!Directory.Exists(_backupDirectory))
        {
            return backups;
        }

        var files = Directory.GetFiles(_backupDirectory, "hosts_backup_*.txt");
        foreach (var file in files)
        {
            var metadataPath = file + ".meta";
            if (File.Exists(metadataPath))
            {
                var json = await File.ReadAllTextAsync(metadataPath);
                var metadata = JsonSerializer.Deserialize<HostsBackup>(json);
                if (metadata != null)
                {
                    metadata.BackupPath = file;
                    backups.Add(metadata);
                }
            }
            else
            {
                var fileInfo = new FileInfo(file);
                backups.Add(new HostsBackup
                {
                    BackupTime = fileInfo.LastWriteTime,
                    BackupPath = file,
                    Content = await File.ReadAllTextAsync(file)
                });
            }
        }

        return backups.OrderByDescending(b => b.BackupTime).ToList();
    }

    public async Task<bool> RestoreBackupAsync(string backupPath)
    {
        if (!File.Exists(backupPath))
        {
            return false;
        }

        var content = await File.ReadAllTextAsync(backupPath);

        // 恢复是"整体还原快照"：内容按原文写回，不能被包进本项目的加速标记块，
        // 否则后续"清除加速"会把用户原有内容当成我们的内容一并删除。
        return await _hostsFileService.ReplaceHostsFileAsync(content);
    }

    public async Task<bool> DeleteBackupAsync(string backupPath)
    {
        try
        {
            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }

            var metadataPath = backupPath + ".meta";
            if (File.Exists(metadataPath))
            {
                File.Delete(metadataPath);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public void CleanupOldBackups(int keepCount = 5)
    {
        var backups = GetAllBackupsAsync().Result;
        if (backups.Count > keepCount)
        {
            var toDelete = backups.Skip(keepCount).ToList();
            foreach (var backup in toDelete)
            {
                DeleteBackupAsync(backup.BackupPath).Wait();
            }
        }
    }

    private void SaveBackupMetadata(HostsBackup backup)
    {
        var metadataPath = backup.BackupPath + ".meta";
        var json = JsonSerializer.Serialize(backup, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(metadataPath, json);
    }
}
