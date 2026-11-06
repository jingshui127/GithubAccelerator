using Avalonia.Controls;
using Avalonia.Interactivity;
using GithubAccelerator.UI.ViewModels;

namespace GithubAccelerator.UI.Views;

public partial class OfficialSiteDiagnosticsView : UserControl
{
    public OfficialSiteDiagnosticsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is OfficialSiteDiagnosticsViewModel vm)
        {
            await vm.EnsureLoadedAsync();
        }
    }
}
