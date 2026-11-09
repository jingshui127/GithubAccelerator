using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using GithubAccelerator.UI.ViewModels;

namespace GithubAccelerator.UI.Views;

public partial class CheckAndRepairView : UserControl
{
    public CheckAndRepairView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CheckAndRepairViewModel vm)
        {
            vm.SetClipboardTextAsync = async text =>
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel?.Clipboard != null)
                {
                    await topLevel.Clipboard.SetTextAsync(text);
                }
            };
        }
    }
}
