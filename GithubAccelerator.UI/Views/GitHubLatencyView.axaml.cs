using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Markup.Xaml;
using GithubAccelerator.UI.Services;
using GithubAccelerator.UI.ViewModels;

namespace GithubAccelerator.UI.Views
{
    public partial class GitHubLatencyView : UserControl
    {
        public GitHubLatencyView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
