using System;
using System.Collections.ObjectModel;
using System.IO;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace GithubAccelerator.UI.Services;

public class LogEntry
{
    public DateTime Timestamp { get; set; }
    public string Level { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Exception { get; set; }

    public string FormattedTime => Timestamp.ToString("HH:mm:ss");
    
    public Avalonia.Media.IBrush? LevelColor => Level switch
    {
        "Debug" => Avalonia.Media.Brushes.Gray,
        "Information" => Avalonia.Media.Brushes.Green,
        "Warning" => Avalonia.Media.Brushes.Orange,
        "Error" => Avalonia.Media.Brushes.Red,
        "Fatal" => Avalonia.Media.Brushes.DarkRed,
        _ => Avalonia.Media.Brushes.Black
    };
}

public class LogService : IDisposable
{
    private readonly ObservableCollection<LogEntry> _logEntries = new();
    private readonly object _lock = new();
    private bool _disposed;
    private const int MaxLogEntries = 1000;

    public ObservableCollection<LogEntry> LogEntries => _logEntries;

    public event Action<LogEntry>? OnLogEntryAdded;

    public LogService()
    {
        InitializeSerilog();
    }

    private void InitializeSerilog()
    {
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GithubAccelerator",
            "Logs",
            "log-.txt");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                path: logPath,
                rollingInterval: RollingInterval.Day,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.Sink(new AvaloniaLogSink(OnLogEntryInternal))
            .CreateLogger();
    }

    private void OnLogEntryInternal(LogEvent logEvent)
    {
        var entry = new LogEntry
        {
            Timestamp = logEvent.Timestamp.DateTime,
            Level = logEvent.Level.ToString(),
            Message = logEvent.RenderMessage(),
            Exception = logEvent.Exception?.ToString()
        };

        lock (_lock)
        {
            if (_logEntries.Count >= MaxLogEntries)
            {
                _logEntries.RemoveAt(0);
            }
            _logEntries.Add(entry);
        }

        OnLogEntryAdded?.Invoke(entry);
    }

    public void Clear()
    {
        lock (_lock)
        {
            _logEntries.Clear();
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Log.CloseAndFlush();
            _disposed = true;
        }
    }
}

public class AvaloniaLogSink : ILogEventSink
{
    private readonly Action<LogEvent> _write;

    public AvaloniaLogSink(Action<LogEvent> write)
    {
        _write = write;
    }

    public void Emit(LogEvent logEvent)
    {
        _write(logEvent);
    }
}
