using System;
using System.Collections.ObjectModel;
using System.IO;
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
    private readonly Action<string>? _onStatusUpdate;

    [ObservableProperty]
    private string _backupTimeText = string.Empty;

    [ObservableProperty]
    private string _note = string.Empty;

    [ObservableProperty]
    private bool _hasNote;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public Func<string, string, Task>? ShowContentDialogAsync { get; set; }

    public BackupItemViewModel(HostsBackup backup, IHostsBackupService backupService,
        Action? onRefresh = null, Action<string>? onStatusUpdate = null)
    {
        _backup = backup;
        _backupService = backupService;
        _onRefresh = onRefresh;
        _onStatusUpdate = onStatusUpdate;

        BackupTimeText = backup.BackupTime.ToString("yyyy-MM-dd HH:mm:ss");
        Note = backup.Note ?? string.Empty;
        HasNote = !string.IsNullOrEmpty(Note);
    }

    private void UpdateStatus(string message)
    {
        StatusMessage = message;
        _onStatusUpdate?.Invoke(message);
    }

    [RelayCommand]
    private async Task Restore()
    {
        try
        {
            var success = await _backupService.RestoreBackupAsync(_backup.BackupPath);
            if (success)
            {
                UpdateStatus("已恢复备份");
                Serilog.Log.Information("备份恢复成功：{BackupPath}", _backup.BackupPath);
            }
            else
            {
                UpdateStatus("恢复失败");
                Serilog.Log.Warning("备份恢复失败：{BackupPath}", _backup.BackupPath);
            }
        }
        catch (Exception ex)
        {
            UpdateStatus($"恢复失败：{ex.Message}");
            Serilog.Log.Warning(ex, "恢复备份失败");
        }
    }

    [RelayCommand]
    private async Task ViewContentAsync()
    {
        try
        {
            if (!File.Exists(_backup.BackupPath))
            {
                UpdateStatus("备份文件不存在");
                return;
            }
            var content = await File.ReadAllTextAsync(_backup.BackupPath);
            if (ShowContentDialogAsync != null)
            {
                await ShowContentDialogAsync($"备份内容 - {BackupTimeText}", content);
            }
            else
            {
                UpdateStatus("查看功能未配置");
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "查看备份内容失败");
            UpdateStatus($"查看失败：{ex.Message}");
        }
    }

    [RelayCommand]
    private async Task Delete()
    {
        try
        {
            var success = await _backupService.DeleteBackupAsync(_backup.BackupPath);
            if (success)
            {
                UpdateStatus("已删除备份");
                Serilog.Log.Information("备份删除成功：{BackupPath}", _backup.BackupPath);
                _onRefresh?.Invoke();
            }
            else
            {
                UpdateStatus("删除失败");
                Serilog.Log.Warning("备份删除失败：{BackupPath}", _backup.BackupPath);
            }
        }
        catch (Exception ex)
        {
            UpdateStatus($"删除失败：{ex.Message}");
            Serilog.Log.Warning(ex, "删除备份失败");
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

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public Func<string, string, Task>? ShowContentDialogAsync { get; set; }

    /// <summary>
    /// 是否存在备份。当处于加载中或存在备份条目时为 false，仅当加载完成且列表为空时为 true。
    /// 用于驱动空状态 UI。
    /// </summary>
    public bool HasNoBackups => !IsLoading && Backups.Count == 0;

    public bool HasBackups => Backups.Count > 0;

    partial void OnIsLoadingChanged(bool value) => NotifyBackupsCountChanged();

    partial void OnBackupsChanged(ObservableCollection<BackupItemViewModel> value)
    {
        value.CollectionChanged += (_, _) => NotifyBackupsCountChanged();
        NotifyBackupsCountChanged();
    }

    private void NotifyBackupsCountChanged()
    {
        OnPropertyChanged(nameof(HasNoBackups));
        OnPropertyChanged(nameof(HasBackups));
    }

    public BackupManagerViewModel()
    {
        _hostsFileService = new WindowsHostsFileService();
        _backupService = new HostsBackupService(_hostsFileService);

        // 订阅初始集合变化以驱动空状态 UI
        Backups.CollectionChanged += (_, _) => NotifyBackupsCountChanged();

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
                var item = new BackupItemViewModel(
                    backup,
                    _backupService,
                    async () => await LoadBackupsAsync(),
                    msg => StatusMessage = msg);
                // 使用闭包，使子项调用时实时读取父类最新的 ShowContentDialogAsync
                item.ShowContentDialogAsync = (title, content) =>
                    ShowContentDialogAsync?.Invoke(title, content) ?? Task.CompletedTask;
                Backups.Add(item);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "加载备份列表失败");
            StatusMessage = $"加载备份失败：{ex.Message}";
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
            StatusMessage = "已创建备份";
            Serilog.Log.Information("创建备份成功");
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "创建备份失败");
            StatusMessage = $"创建备份失败：{ex.Message}";
        }
    }
}
