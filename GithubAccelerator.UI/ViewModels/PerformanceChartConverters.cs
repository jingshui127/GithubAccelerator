using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using GithubAccelerator.UI.ViewModels;

namespace GithubAccelerator.UI.ViewModels;

public class ChartTypeBackgroundConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ChartType currentType && parameter is string paramStr &&
            int.TryParse(paramStr, out var targetIdx) && currentType == (ChartType)targetIdx)
        {
            return new SolidColorBrush(Color.Parse("#2196F3"), 0.15);
        }
        return new SolidColorBrush(Colors.Transparent);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class IsScoreDistributionConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is ChartType type && type == ChartType.ScoreDistribution;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
