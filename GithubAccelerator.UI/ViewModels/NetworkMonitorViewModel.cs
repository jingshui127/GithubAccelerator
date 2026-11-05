using CommunityToolkit.Mvvm.ComponentModel;

namespace GithubAccelerator.UI.ViewModels;

public partial class NetworkMonitorViewModel : ObservableObject
{
    [ObservableProperty]
    private GitHubLatencyViewModel _gitHubLatency = new();
}
