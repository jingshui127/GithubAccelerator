using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GithubAccelerator.UI.ViewModels;

/// <summary>「回到原始状态」单项：名称 + 将还原什么 + 当前是否真的应用了。</summary>
public partial class RestoreItemViewModel : ObservableObject
{
    public RestoreItemViewModel(string key, string name, string detail)
    {
        Key = key;
        Name = name;
        Detail = detail;
    }

    public string Key { get; }

    public string Name { get; }

    public string Detail { get; }

    [ObservableProperty]
    private bool _isSelected = true;

    [ObservableProperty]
    private bool _isAvailable = true;

    [ObservableProperty]
    private string _stateText = string.Empty;
}

/// <summary>
/// 「回到原始状态」统一入口：把原来的 Hosts 清除 / Git 停止 / 防护版撤销 / 首启快照恢复
/// 合并为一个动作。执行前逐项列出将还原什么，并要求一次确认；不静默提权。
/// </summary>
public partial class RestoreOriginalStateViewModel : ObservableObject
{
    private readonly MainWindowViewModel _host;
    private readonly SettingsViewModel _settings;
    private readonly OfficialSiteHostsGuardViewModel _guard;
    private readonly BackupManagerViewModel _backup;

    public RestoreOriginalStateViewModel(
        MainWindowViewModel host,
        SettingsViewModel settings,
        OfficialSiteHostsGuardViewModel guard,
        BackupManagerViewModel backup)
    {
        _host = host;
        _settings = settings;
        _guard = guard;
        _backup = backup;

        Items = new ObservableCollection<RestoreItemViewModel>
        {
            new("hosts", "Hosts 加速块", "移除本程序写入的 GitHub 加速块（块外内容不动）"),
            new("git", "Git 地址改写", "删除本程序写入的 url.<加速地址>.insteadOf，clone 地址恢复官方"),
            new("guard", "固定 GitHub 地址（防护版）", "撤销只针对 github.com 的固定 IP 映射"),
            new("snapshot", "首启前 Hosts 快照", "用首次加速前的完整快照覆盖当前 Hosts（没有快照时会明确提示）")
        };

        RefreshStatus();
    }

    public ObservableCollection<RestoreItemViewModel> Items { get; }

    [ObservableProperty]
    private bool _isConfirming;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "勾选要还原的项目，点「预览将还原什么」；确认前不会修改任何系统设置。";

    [ObservableProperty]
    private string _previewText = string.Empty;

    public bool HasPreview => !string.IsNullOrEmpty(PreviewText);

    partial void OnPreviewTextChanged(string value) => OnPropertyChanged(nameof(HasPreview));

    /// <summary>刷新每项的“当前是否已应用”，避免把已还原的项目列成待还原。</summary>
    public void RefreshStatus()
    {
        Set("hosts", _host.IsHostsApplied, _host.IsHostsApplied ? "当前已应用" : "当前未应用，无需还原");
        var gitOwned = _settings.GitRewriteStatus.StartsWith("已启用（本程序写入）", System.StringComparison.Ordinal);
        Set("git", gitOwned, gitOwned ? "当前已应用" : "当前未应用，无需还原");
        Set("guard", _guard.IsApplied, _guard.IsApplied ? "当前已应用" : "当前未应用，无需还原");
        Set("snapshot", true, _backup.HasBackups ? "有可用备份" : "尚无备份记录");
    }

    private void Set(string key, bool available, string state)
    {
        var item = Items.FirstOrDefault(i => i.Key == key);
        if (item == null) return;
        item.IsAvailable = available;
        item.StateText = state;
        if (!available) item.IsSelected = false;
    }

    [RelayCommand]
    private void PreviewRestore()
    {
        RefreshStatus();
        var selected = Items.Where(i => i.IsAvailable && i.IsSelected).ToList();
        if (selected.Count == 0)
        {
            IsConfirming = false;
            PreviewText = string.Empty;
            StatusMessage = "当前没有需要还原的项目（或未勾选任何项）。";
            return;
        }

        PreviewText = "将还原以下项目：\n" +
                      string.Join("\n", selected.Select(i => $"• {i.Name}：{i.Detail}")) +
                      "\n\n执行需要管理员权限；本程序不会静默提权。块外内容不会被修改。";
        IsConfirming = true;
        StatusMessage = "请确认后的清单，再点「确认还原」。";
    }

    [RelayCommand]
    private async Task ConfirmRestore()
    {
        if (IsBusy || !IsConfirming) return;

        IsBusy = true;
        try
        {
            var done = new System.Collections.Generic.List<string>();

            if (Items.First(i => i.Key == "hosts").IsSelected && Items.First(i => i.Key == "hosts").IsAvailable)
            {
                await _host.ClearAppliedHostsCommand.ExecuteAsync(null);
                done.Add("Hosts 加速块");
            }

            if (Items.First(i => i.Key == "git").IsSelected && Items.First(i => i.Key == "git").IsAvailable)
            {
                await _settings.DisableGitRewriteCommand.ExecuteAsync(null);
                done.Add("Git 地址改写");
            }

            if (Items.First(i => i.Key == "guard").IsSelected && Items.First(i => i.Key == "guard").IsAvailable)
            {
                await _guard.UndoConfirmedAsync();
                done.Add("固定 GitHub 地址");
            }

            if (Items.First(i => i.Key == "snapshot").IsSelected && Items.First(i => i.Key == "snapshot").IsAvailable)
            {
                var ok = await _backup.RestoreFirstRunSnapshotConfirmedAsync();
                done.Add(ok ? "首启前 Hosts 快照" : "首启前快照（未找到或失败）");
            }

            StatusMessage = done.Count == 0
                ? "没有执行任何还原项。"
                : "已还原：" + string.Join("、", done) + "。请重新检查状态。";
        }
        catch (System.Exception ex)
        {
            StatusMessage = "还原失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
            IsConfirming = false;
            PreviewText = string.Empty;
            RefreshStatus();
        }
    }

    [RelayCommand]
    private void CancelRestore()
    {
        IsConfirming = false;
        PreviewText = string.Empty;
        StatusMessage = "已取消，未修改任何系统设置。";
    }
}
