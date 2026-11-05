using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using GithubAccelerator.UI.ViewModels;

namespace GithubAccelerator.UI.Views;

public partial class DownloadAcceleratorView : UserControl
{
    public DownloadAcceleratorView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DownloadAcceleratorViewModel vm)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard != null)
            {
                var clipboard = topLevel.Clipboard;
                vm.GetClipboardTextAsync = async () => await clipboard.TryGetTextAsync();
                vm.SetClipboardTextAsync = async text => await clipboard.SetTextAsync(text);
            }
        }
    }
}
