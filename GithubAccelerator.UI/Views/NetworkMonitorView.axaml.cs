using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GithubAccelerator.UI.Views;

public partial class NetworkMonitorView : UserControl
{
    public NetworkMonitorView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
