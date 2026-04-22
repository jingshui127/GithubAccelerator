using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GithubAccelerator.UI.Services;

namespace GithubAccelerator.UI.ViewModels;

public partial class HostsGroupViewModel : ObservableObject
{
    private readonly HostsGroupService _groupService;
    private readonly OperationHistoryService _historyService;
    private readonly NotificationService _notificationService;

    [ObservableProperty]
    private ObservableCollection<HostsGroup> _groups = new();

    [ObservableProperty]
    private HostsGroup? _selectedGroup;

    [ObservableProperty]
    private string _newGroupName = string.Empty;

    [ObservableProperty]
    private string _newGroupDescription = string.Empty;

    [ObservableProperty]
    private string _newEntryIp = string.Empty;

    [ObservableProperty]
    private string _newEntryDomain = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public HostsGroupViewModel()
    {
        _groupService = HostsGroupService.Instance;
        _historyService = OperationHistoryService.Instance;
        _notificationService = NotificationService.Instance;

        LoadGroups();
        _groupService.OnGroupsChanged += OnGroupsChanged;
    }

    private void OnGroupsChanged()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => LoadGroups());
    }

    private void LoadGroups()
    {
        Groups.Clear();
        foreach (var group in _groupService.Groups)
        {
            Groups.Add(group);
        }

        if (SelectedGroup == null && Groups.Count > 0)
        {
            SelectedGroup = Groups[0];
        }
    }

    [RelayCommand]
    private void CreateGroup()
    {
        if (string.IsNullOrWhiteSpace(NewGroupName))
        {
            StatusMessage = "请输入分组名称";
            return;
        }

        var group = _groupService.CreateGroup(NewGroupName, NewGroupDescription);
        NewGroupName = string.Empty;
        NewGroupDescription = string.Empty;
        
        // 添加到列表并选中
        Groups.Add(group);
        SelectedGroup = group;
        StatusMessage = $"已创建分组：{group.Name}";

        _historyService.Record(OperationType.SettingsChanged, $"创建 Hosts 分组：{group.Name}");
        _notificationService.Success("分组管理", StatusMessage);
    }

    [RelayCommand]
    private void DeleteGroup()
    {
        if (SelectedGroup == null)
        {
            StatusMessage = "请先选择一个分组";
            return;
        }

        var name = SelectedGroup.Name;
        var groupId = SelectedGroup.Id;
        _groupService.DeleteGroup(groupId);
        
        // 从列表中移除并选择第一个分组
        var groupToRemove = Groups.FirstOrDefault(g => g.Id == groupId);
        if (groupToRemove != null)
        {
            Groups.Remove(groupToRemove);
        }
        SelectedGroup = Groups.FirstOrDefault();
        
        StatusMessage = $"已删除分组：{name}";

        _historyService.Record(OperationType.SettingsChanged, $"删除 Hosts 分组：{name}");
        _notificationService.Info("分组管理", StatusMessage);
    }

    [RelayCommand]
    private void RenameGroup()
    {
        if (SelectedGroup == null)
        {
            StatusMessage = "请先选择一个分组";
            return;
        }

        if (string.IsNullOrWhiteSpace(NewGroupName))
        {
            StatusMessage = "请输入新的分组名称";
            return;
        }

        var oldName = SelectedGroup.Name;
        _groupService.UpdateGroup(SelectedGroup.Id, name: NewGroupName);
        
        // 刷新选中的分组
        var updatedGroup = _groupService.GetGroup(SelectedGroup.Id);
        if (updatedGroup != null)
        {
            var index = Groups.IndexOf(SelectedGroup);
            if (index >= 0)
            {
                Groups[index] = updatedGroup;
                SelectedGroup = updatedGroup;
            }
        }
        
        StatusMessage = $"已重命名分组：{oldName} → {NewGroupName}";
        NewGroupName = string.Empty;

        _historyService.Record(OperationType.SettingsChanged, $"重命名 Hosts 分组：{oldName} → {NewGroupName}");
        _notificationService.Success("分组管理", StatusMessage);
    }

    [RelayCommand]
    private void ToggleGroup()
    {
        if (SelectedGroup == null) return;

        var newStatus = !SelectedGroup.IsEnabled;
        _groupService.UpdateGroup(SelectedGroup.Id, isEnabled: newStatus);
        StatusMessage = newStatus ? $"已启用分组：{SelectedGroup.Name}" : $"已禁用分组：{SelectedGroup.Name}";
        
        // 刷新选中的分组以更新 UI
        var updatedGroup = _groupService.GetGroup(SelectedGroup.Id);
        if (updatedGroup != null)
        {
            var index = Groups.IndexOf(SelectedGroup);
            if (index >= 0)
            {
                Groups[index] = updatedGroup;
                SelectedGroup = updatedGroup;
            }
        }

        _historyService.Record(OperationType.SettingsChanged, $"{(newStatus ? "启用" : "禁用")} Hosts 分组：{SelectedGroup.Name}");
        _notificationService.Info("分组管理", StatusMessage);
    }

    [RelayCommand]
    private void AddEntry()
    {
        if (SelectedGroup == null)
        {
            StatusMessage = "请先选择一个分组";
            return;
        }

        if (string.IsNullOrWhiteSpace(NewEntryIp) || string.IsNullOrWhiteSpace(NewEntryDomain))
        {
            StatusMessage = "请输入 IP 和域名";
            return;
        }

        if (!System.Net.IPAddress.TryParse(NewEntryIp, out _))
        {
            StatusMessage = "IP 地址格式不正确";
            return;
        }

        var entry = new HostsEntry
        {
            Ip = NewEntryIp,
            Domain = NewEntryDomain,
            IsEnabled = true,
            Comment = string.Empty
        };

        _groupService.AddEntry(SelectedGroup.Id, entry);
        NewEntryIp = string.Empty;
        NewEntryDomain = string.Empty;
        StatusMessage = $"已添加：{entry.Ip} → {entry.Domain}";

        // 刷新选中的分组
        var updatedGroup = _groupService.GetGroup(SelectedGroup.Id);
        if (updatedGroup != null)
        {
            var index = Groups.IndexOf(SelectedGroup);
            if (index >= 0)
            {
                Groups[index] = updatedGroup;
                SelectedGroup = updatedGroup;
            }
        }

        _historyService.Record(OperationType.SettingsChanged, $"添加 Hosts 规则：{entry.Domain}");
        _notificationService.Success("分组管理", StatusMessage);
    }

    [RelayCommand]
    private void RemoveEntry(HostsEntry entry)
    {
        if (SelectedGroup == null || entry == null) return;

        var domain = entry.Domain;
        _groupService.RemoveEntry(SelectedGroup.Id, domain);
        StatusMessage = $"已移除：{domain}";

        // 刷新选中的分组
        var updatedGroup = _groupService.GetGroup(SelectedGroup.Id);
        if (updatedGroup != null)
        {
            var index = Groups.IndexOf(SelectedGroup);
            if (index >= 0)
            {
                Groups[index] = updatedGroup;
                SelectedGroup = updatedGroup;
            }
        }

        _historyService.Record(OperationType.SettingsChanged, $"移除 Hosts 规则：{domain}");
        _notificationService.Info("分组管理", StatusMessage);
    }

    [RelayCommand]
    private void ToggleEntry(HostsEntry entry)
    {
        if (SelectedGroup == null || entry == null) return;

        var newStatus = !entry.IsEnabled;
        _groupService.ToggleEntry(SelectedGroup.Id, entry.Domain, newStatus);
        StatusMessage = newStatus ? $"已启用：{entry.Domain}" : $"已禁用：{entry.Domain}";

        // 刷新选中的分组
        var updatedGroup = _groupService.GetGroup(SelectedGroup.Id);
        if (updatedGroup != null)
        {
            var index = Groups.IndexOf(SelectedGroup);
            if (index >= 0)
            {
                Groups[index] = updatedGroup;
                SelectedGroup = updatedGroup;
            }
        }

        _historyService.Record(OperationType.SettingsChanged, $"{(newStatus ? "启用" : "禁用")} Hosts 规则：{entry.Domain}");
        _notificationService.Info("分组管理", StatusMessage);
    }

    [RelayCommand]
    private async Task ApplyGroupsAsync()
    {
        IsLoading = true;
        StatusMessage = "正在应用分组规则...";

        try
        {
            var content = _groupService.GenerateHostsContent();
            if (string.IsNullOrWhiteSpace(content))
            {
                StatusMessage = "没有可应用的分组规则";
                _notificationService.Warning("分组管理", "没有可应用的分组规则");
                return;
            }

            var hostsPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "drivers", "etc", "hosts");

            var currentContent = await System.IO.File.ReadAllTextAsync(hostsPath);

            var lines = currentContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(l => !l.TrimStart().StartsWith("# Group:") && !l.Contains("# Group:"))
                .ToList();

            lines.Add(string.Empty);
            lines.Add(content);

            await System.IO.File.WriteAllTextAsync(hostsPath, string.Join(Environment.NewLine, lines));

            StatusMessage = "分组规则已成功应用";
            _historyService.Record(OperationType.HostsApplied, "应用 Hosts 分组规则", true);
            _notificationService.Success("分组管理", "分组规则已成功应用");
        }
        catch (UnauthorizedAccessException)
        {
            StatusMessage = "权限不足，请以管理员身份运行";
            _historyService.Record(OperationType.HostsApplied, "应用分组规则失败：权限不足", false);
            _notificationService.Error("分组管理", "权限不足，请以管理员身份运行");
        }
        catch (Exception ex)
        {
            StatusMessage = $"应用失败：{ex.Message}";
            _historyService.Record(OperationType.HostsApplied, $"应用分组规则失败：{ex.Message}", false);
            _notificationService.Error("分组管理", $"应用失败：{ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void ImportFromCurrentHosts()
    {
        try
        {
            var hostsPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "drivers", "etc", "hosts");
            var content = System.IO.File.ReadAllText(hostsPath);

            _groupService.ImportFromHostsContent(content, "当前 Hosts 导入");
            LoadGroups();
            StatusMessage = "已从当前 Hosts 文件导入";

            _historyService.Record(OperationType.SettingsChanged, "从当前 Hosts 导入分组");
            _notificationService.Success("分组管理", "已从当前 Hosts 文件导入");
        }
        catch (Exception ex)
        {
            StatusMessage = $"导入失败：{ex.Message}";
            _notificationService.Error("分组管理", $"导入失败：{ex.Message}");
        }
    }
}
