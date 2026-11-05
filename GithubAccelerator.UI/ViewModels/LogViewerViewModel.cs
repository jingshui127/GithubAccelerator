using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GithubAccelerator.UI.Services;
using Serilog;

namespace GithubAccelerator.UI.ViewModels;

public partial class LogEntryViewModel : ObservableObject
{
    private readonly LogEntry _logEntry;

    [ObservableProperty]
    private string _formattedTime = string.Empty;

    [ObservableProperty]
    private string _level = string.Empty;

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private bool _hasException;

    [ObservableProperty]
    private IBrush _levelBackground = Brushes.Gray;

    /// <summary>
    /// 由 View 层设置的剪贴板设置委托，避免 ViewModel 直接依赖 UI 框架。
    /// </summary>
    public Func<string, Task>? SetClipboardTextAsync { get; set; }

    public LogEntryViewModel(LogEntry logEntry)
    {
        _logEntry = logEntry;
        FormattedTime = logEntry.FormattedTime;
        Level = logEntry.Level;
        Message = logEntry.Message;
        HasException = !string.IsNullOrEmpty(logEntry.Exception);

        LevelBackground = Level switch
        {
            "Debug" => Brushes.Gray,
            "Information" => Brushes.Green,
            "Warning" => Brushes.Orange,
            "Error" => Brushes.Red,
            "Fatal" => Brushes.DarkRed,
            _ => Brushes.Gray
        };
    }

    [RelayCommand]
    private async Task CopyMessage()
    {
        try
        {
            if (SetClipboardTextAsync != null)
            {
                await SetClipboardTextAsync(Message);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"复制失败：{ex.Message}");
        }
    }
}

public partial class LogViewerViewModel : ObservableObject
{
    private readonly LogService _logService;
    private string _selectedLogLevel = "全部";

    [ObservableProperty]
    private ObservableCollection<LogEntryViewModel> _logs = new();

    [ObservableProperty]
    private ObservableCollection<LogEntryViewModel> _filteredLogs = new();

    /// <summary>
    /// 关键词搜索文本（不区分大小写）。空字符串表示不搜索。
    /// </summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>
    /// 当筛选后的日志列表为空时返回 true，用于驱动空状态 UI。
    /// </summary>
    public bool HasNoLogs => FilteredLogs == null || FilteredLogs.Count == 0;

    /// <summary>
    /// ComboBox 数据源：日志级别筛选选项。
    /// </summary>
    public ObservableCollection<string> LogLevels { get; } = new()
    {
        "全部", "Debug", "Information", "Warning", "Error"
    };

    /// <summary>
    /// 由 View 层设置的剪贴板委托，传递给子条目。
    /// 设置时会同步更新已存在的所有日志条目，确保历史日志也能复制。
    /// </summary>
    public Func<string, Task>? SetClipboardTextAsync
    {
        get => _setClipboardTextAsync;
        set
        {
            _setClipboardTextAsync = value;
            foreach (var log in Logs)
            {
                log.SetClipboardTextAsync = value;
            }
        }
    }
    private Func<string, Task>? _setClipboardTextAsync;

    /// <summary>
    /// 由 View 层设置的导出对话框委托：参数为 (默认文件名, 文件内容)，返回用户选择的文件路径或 null。
    /// </summary>
    public Func<string, string, Task<string?>>? SaveFileAsync { get; set; }

    public string SelectedLogLevel
    {
        get => _selectedLogLevel;
        set
        {
            SetProperty(ref _selectedLogLevel, value);
            FilterLogs();
        }
    }

    public LogViewerViewModel()
    {
        _logService = LogService.Instance;
        _logService.OnLogEntryAdded += OnLogEntryAdded;

        foreach (var entry in _logService.LogEntries)
        {
            Logs.Add(CreateLogEntry(entry));
        }

        FilteredLogs = new ObservableCollection<LogEntryViewModel>(Logs);
    }

    partial void OnSearchTextChanged(string value)
    {
        FilterLogs();
    }

    private LogEntryViewModel CreateLogEntry(LogEntry entry)
    {
        return new LogEntryViewModel(entry)
        {
            SetClipboardTextAsync = SetClipboardTextAsync
        };
    }

    private void OnLogEntryAdded(LogEntry entry)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var viewModel = CreateLogEntry(entry);
            Logs.Add(viewModel);
            FilterLogs();
        });
    }

    private void FilterLogs()
    {
        FilteredLogs.Clear();

        // 1. 按级别筛选
        var byLevel = string.IsNullOrEmpty(SelectedLogLevel) || SelectedLogLevel == "全部"
            ? Logs
            : Logs.Where(l => l.Level == SelectedLogLevel);

        // 2. 按关键词筛选（不区分大小写，匹配时间/级别/消息）
        var keyword = SearchText?.Trim() ?? string.Empty;
        var finalLogs = string.IsNullOrEmpty(keyword)
            ? byLevel
            : byLevel.Where(l =>
                (l.Message?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false)
                || (l.Level?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false)
                || (l.FormattedTime?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false));

        foreach (var log in finalLogs)
        {
            FilteredLogs.Add(log);
        }

        OnPropertyChanged(nameof(HasNoLogs));
    }

    [RelayCommand]
    private void ClearLogs()
    {
        _logService.Clear();
        Logs.Clear();
        FilteredLogs.Clear();
        Log.Information("日志已清空");
    }

    /// <summary>
    /// 导出当前筛选后的日志到 txt 文件。
    /// </summary>
    [RelayCommand]
    private async Task ExportLogs()
    {
        if (FilteredLogs.Count == 0)
        {
            return;
        }

        if (SaveFileAsync == null)
        {
            return;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# GitHub 加速器日志导出");
        sb.AppendLine($"# 导出时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"# 日志条数: {FilteredLogs.Count}");
        sb.AppendLine($"# 筛选条件: 级别={SelectedLogLevel}, 关键词=\"{SearchText}\"");
        sb.AppendLine();

        foreach (var log in FilteredLogs)
        {
            sb.AppendLine($"[{log.FormattedTime}] [{log.Level}] {log.Message}");
        }

        var defaultName = $"github_accelerator_logs_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
        var path = await SaveFileAsync(defaultName, sb.ToString());
        if (!string.IsNullOrEmpty(path))
        {
            Log.Information("日志已导出到：{Path}", path);
        }
    }
}
