using System;
using System.Collections.Specialized;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using GithubAccelerator.UI.ViewModels;

namespace GithubAccelerator.UI.Views;

public partial class LogViewerView : UserControl
{
    private bool _autoScroll = true;

    public LogViewerView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is LogViewerViewModel vm)
        {
            // 剪贴板委托
            vm.SetClipboardTextAsync = async text =>
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel?.Clipboard != null)
                {
                    await topLevel.Clipboard.SetTextAsync(text);
                }
            };

            // 文件保存对话框委托
            vm.SaveFileAsync = async (defaultFileName, content) =>
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel == null) return null;

                var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "导出日志",
                    DefaultExtension = "txt",
                    SuggestedFileName = defaultFileName,
                    FileTypeChoices = new[]
                    {
                        new FilePickerFileType("文本文件")
                        {
                            Patterns = new[] { "*.txt" }
                        },
                        new FilePickerFileType("所有文件")
                        {
                            Patterns = new[] { "*.*" }
                        }
                    }
                });

                if (file != null)
                {
                    await using var stream = await file.OpenWriteAsync();
                    using var writer = new StreamWriter(stream);
                    await writer.WriteAsync(content);
                    return file.Path.LocalPath;
                }

                return null;
            };

            // 订阅日志集合变化以实现自动滚动
            vm.FilteredLogs.CollectionChanged += OnFilteredLogsChanged;
        }
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is LogViewerViewModel vm)
        {
            vm.FilteredLogs.CollectionChanged -= OnFilteredLogsChanged;
        }
    }

    private void OnFilteredLogsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_autoScroll && e.Action == NotifyCollectionChangedAction.Add && LogsListBox.ItemCount > 0)
        {
            Dispatcher.UIThread.Post(() =>
            {
                LogsListBox.ScrollIntoView(LogsListBox.ItemCount - 1);
            }, DispatcherPriority.Background);
        }
    }
}
