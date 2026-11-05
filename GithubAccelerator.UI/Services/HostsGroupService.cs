using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GithubAccelerator.UI.Services;

public class HostsEntry
{
    public string Ip { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;

    public string ToHostsLine()
    {
        var line = IsEnabled ? $"{Ip}\t{Domain}" : $"# {Ip}\t{Domain}";
        if (!string.IsNullOrEmpty(Comment))
        {
            line += $" # {Comment}";
        }
        return line;
    }

    public static HostsEntry? Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        var trimmed = line.Trim();
        if (trimmed.StartsWith('#'))
        {
            trimmed = trimmed.Substring(1).Trim();
        }

        var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;

        if (!System.Net.IPAddress.TryParse(parts[0], out _)) return null;

        return new HostsEntry
        {
            Ip = parts[0],
            Domain = parts[1],
            IsEnabled = !line.TrimStart().StartsWith('#'),
            Comment = parts.Length > 2 ? string.Join(" ", parts.Skip(2)) : string.Empty
        };
    }
}

public class HostsGroup
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Color { get; set; } = "#2196F3";
    public bool IsEnabled { get; set; } = true;
    public List<HostsEntry> Entries { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public string? SourceUrl { get; set; }

    public string ToHostsBlock()
    {
        var lines = new List<string>
        {
            $"# Group:{Name} Start",
        };

        foreach (var entry in Entries)
        {
            lines.Add(entry.ToHostsLine());
        }

        lines.Add($"# Group:{Name} End");
        return string.Join(Environment.NewLine, lines);
    }

    public int EnabledCount => Entries.Count(e => e.IsEnabled);
    public int TotalCount => Entries.Count;
}

public class HostsGroupService
{
    private static readonly Lazy<HostsGroupService> _instance = new(() => new HostsGroupService());
    public static HostsGroupService Instance => _instance.Value;

    private readonly List<HostsGroup> _groups = new();
    private readonly object _gate = new();
    private readonly string _configPath;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public IReadOnlyList<HostsGroup> Groups => _groups.AsReadOnly();
    public event Action? OnGroupsChanged;

    /// <summary>
    /// 线程安全的深拷贝快照。导出/序列化必须用它，避免与其它线程的增删并发时抛出
    /// "Collection was modified"（ExportImport_Roundtrip 偶发失败的根因）。
    /// </summary>
    public List<HostsGroup> Snapshot()
    {
        lock (_gate)
        {
            return _groups.Select(CloneGroup).ToList();
        }
    }

    private static HostsGroup CloneGroup(HostsGroup source)
    {
        return new HostsGroup
        {
            Id = source.Id,
            Name = source.Name,
            Description = source.Description,
            Color = source.Color,
            IsEnabled = source.IsEnabled,
            CreatedAt = source.CreatedAt,
            UpdatedAt = source.UpdatedAt,
            SourceUrl = source.SourceUrl,
            Entries = source.Entries.Select(e => new HostsEntry
            {
                Ip = e.Ip,
                Domain = e.Domain,
                Comment = e.Comment,
                IsEnabled = e.IsEnabled
            }).ToList()
        };
    }

    public HostsGroupService()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GithubAccelerator");
        Directory.CreateDirectory(appDataPath);
        _configPath = Path.Combine(appDataPath, "hosts_groups.json");
        LoadFromFile();
    }

    public HostsGroup CreateGroup(string name, string description = "", string color = "#2196F3")
    {
        var group = new HostsGroup
        {
            Name = name,
            Description = description,
            Color = color,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };

        lock (_gate)
        {
            _groups.Add(group);
            SaveToFile();
        }
        OnGroupsChanged?.Invoke();
        return group;
    }

    public bool UpdateGroup(string groupId, string? name = null, string? description = null, string? color = null, bool? isEnabled = null)
    {
        bool changed;
        lock (_gate)
        {
            var group = _groups.FirstOrDefault(g => g.Id == groupId);
            if (group == null) return false;

            if (name != null) group.Name = name;
            if (description != null) group.Description = description;
            if (color != null) group.Color = color;
            if (isEnabled.HasValue) group.IsEnabled = isEnabled.Value;
            group.UpdatedAt = DateTime.Now;

            SaveToFile();
            changed = true;
        }

        OnGroupsChanged?.Invoke();
        return changed;
    }

    public bool DeleteGroup(string groupId)
    {
        lock (_gate)
        {
            var group = _groups.FirstOrDefault(g => g.Id == groupId);
            if (group == null) return false;

            _groups.Remove(group);
            SaveToFile();
        }
        OnGroupsChanged?.Invoke();
        return true;
    }

    public bool AddEntry(string groupId, HostsEntry entry)
    {
        lock (_gate)
        {
            var group = _groups.FirstOrDefault(g => g.Id == groupId);
            if (group == null) return false;

            group.Entries.Add(entry);
            group.UpdatedAt = DateTime.Now;
            SaveToFile();
        }
        OnGroupsChanged?.Invoke();
        return true;
    }

    public bool RemoveEntry(string groupId, string domain)
    {
        bool removedAny;
        lock (_gate)
        {
            var group = _groups.FirstOrDefault(g => g.Id == groupId);
            if (group == null) return false;

            var removed = group.Entries.RemoveAll(e => e.Domain == domain);
            removedAny = removed > 0;
            if (removedAny)
            {
                group.UpdatedAt = DateTime.Now;
                SaveToFile();
            }
        }
        if (removedAny) OnGroupsChanged?.Invoke();
        return removedAny;
    }

    public bool ToggleEntry(string groupId, string domain, bool enabled)
    {
        lock (_gate)
        {
            var group = _groups.FirstOrDefault(g => g.Id == groupId);
            if (group == null) return false;

            var entry = group.Entries.FirstOrDefault(e => e.Domain == domain);
            if (entry == null) return false;

            entry.IsEnabled = enabled;
            group.UpdatedAt = DateTime.Now;
            SaveToFile();
        }
        OnGroupsChanged?.Invoke();
        return true;
    }

    public string GenerateHostsContent()
    {
        var lines = new List<string>();
        foreach (var group in Snapshot().Where(g => g.IsEnabled && g.Entries.Count > 0))
        {
            lines.Add(group.ToHostsBlock());
            lines.Add(string.Empty);
        }
        return string.Join(Environment.NewLine, lines);
    }

    public void ImportFromHostsContent(string content, string groupName = "导入的规则")
    {
        var entries = new List<HostsEntry>();
        var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var entry = HostsEntry.Parse(line);
            if (entry != null)
            {
                entries.Add(entry);
            }
        }

        if (entries.Count > 0)
        {
            var group = CreateGroup(groupName, $"从 Hosts 内容导入，共 {entries.Count} 条规则");
            foreach (var entry in entries)
            {
                group.Entries.Add(entry);
            }
            group.UpdatedAt = DateTime.Now;
            SaveToFile();
            OnGroupsChanged?.Invoke();
        }
    }

    public HostsGroup? GetGroup(string groupId)
    {
        lock (_gate)
        {
            return _groups.FirstOrDefault(g => g.Id == groupId);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _groups.Clear();
            SaveToFile();
        }
        OnGroupsChanged?.Invoke();
    }

    private void SaveToFile()
    {
        try
        {
            var json = JsonSerializer.Serialize(_groups, _jsonOptions);
            File.WriteAllText(_configPath, json);
        }
        catch
        {
        }
    }

    private void LoadFromFile()
    {
        try
        {
            if (!File.Exists(_configPath)) return;
            var json = File.ReadAllText(_configPath);
            var groups = JsonSerializer.Deserialize<List<HostsGroup>>(json, _jsonOptions);
            if (groups != null)
            {
                _groups.Clear();
                _groups.AddRange(groups);
            }
        }
        catch
        {
        }
    }
}
