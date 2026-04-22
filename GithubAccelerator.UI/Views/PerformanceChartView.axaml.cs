using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using GithubAccelerator.UI.ViewModels;

namespace GithubAccelerator.UI.Views
{
    public partial class PerformanceChartView : UserControl
    {
        public PerformanceChartView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
