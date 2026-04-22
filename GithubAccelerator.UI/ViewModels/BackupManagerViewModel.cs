using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GithubAccelerator.Services;
using GithubAccelerator.UI.Services;

namespace GithubAccelerator.UI.ViewModels;

public partial class BackupItemViewModel : ObservableObject
{
    private readonly HostsBackup _backup;
    private readonly IHostsBackupService _backupService;
    private readonly Action? _onRefresh;

    [ObservableProperty]
    private string _backupTimeText = string.Empty;

    [ObservableProperty]
    private string _note = string.Empty;

    [ObservableProperty]
    private bool _hasNote;

    public BackupItemViewModel(HostsBackup backup, IHostsBackupService backupService, Action? onRefresh = null)
    {
        _backup = backup;
        _backupService = backupService;
        _onRefresh = onRefresh;

        BackupTimeText = backup.BackupTime.ToString("yyyy-MM-dd HH:mm:ss");
        Note = backup.Note ?? string.Empty;
        HasNote = !string.IsNullOrEmpty(Note);
    }

    [RelayCommand]
    private async Task Restore()
    {
        var success = await _backupService.RestoreBackupAsync(_backup.BackupPath);
        if (success)
        {
            Console.WriteLine("备份恢复成功");
        }
        else
        {
            Console.WriteLine("备份恢复失败");
        }
    }

    [RelayCommand]
    private void ViewContent()
    {
        Console.WriteLine($"查看备份内容：{_backup.BackupPath}");
    }

    [RelayCommand]
    private async Task Delete()
    {
        var success = await _backupService.DeleteBackupAsync(_backup.BackupPath);
        if (success && _onRefresh != null)
        {
            _onRefresh();
        }
    }
}

public partial class BackupManagerViewModel : ObservableObject
{
    private readonly IHostsBackupService _backupService;
    private readonly IHostsFileService _hostsFileService;

    [ObservableProperty]
    private ObservableCollection<BackupItemViewModel> _backups = new();

    [ObservableProperty]
    private bool _isLoading = true;

    public BackupManagerViewModel()
    {
        _hostsFileService = new WindowsHostsFileService();
        _backupService = new HostsBackupService(_hostsFileService);

        _ = LoadBackupsAsync();
    }

    private async Task LoadBackupsAsync()
    {
        try
        {
            IsLoading = true;
            Backups.Clear();
            var backups = await _backupService.GetAllBackupsAsync();
            foreach (var backup in backups)
            {
                Backups.Add(new BackupItemViewModel(backup, _backupService, async () => await LoadBackupsAsync()));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"加载备份失败：{ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task CreateBackup()
    {
        try
        {
            var note = $"自动备份 - {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            await _backupService.CreateBackupAsync(note);
            await LoadBackupsAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"创建备份失败：{ex.Message}");
        }
    }
}
