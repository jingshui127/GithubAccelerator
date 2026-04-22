using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
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
    private void CopyMessage()
    {
        try
        {
            System.Windows.Forms.Clipboard.SetText(Message);
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

    [ObservableProperty]
    private string _selectedLogLevelProperty = "全部";

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
            Logs.Add(new LogEntryViewModel(entry));
        }

        FilteredLogs = new ObservableCollection<LogEntryViewModel>(Logs);
    }

    private void OnLogEntryAdded(LogEntry entry)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var viewModel = new LogEntryViewModel(entry);
            Logs.Add(viewModel);
            FilterLogs();
        });
    }

    private void OnSelectedLogLevelChanged(string value)
    {
        FilterLogs();
    }

    private void FilterLogs()
    {
        FilteredLogs.Clear();

        var logsToAdd = string.IsNullOrEmpty(SelectedLogLevel) || SelectedLogLevel == "全部"
            ? Logs
            : Logs.Where(l => l.Level == SelectedLogLevel);

        foreach (var log in logsToAdd)
        {
            FilteredLogs.Add(log);
        }
    }

    [RelayCommand]
    private void ClearLogs()
    {
        _logService.Clear();
        Logs.Clear();
        FilteredLogs.Clear();
        Log.Information("日志已清空");
    }
}
