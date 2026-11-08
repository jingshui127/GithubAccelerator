using Avalonia.Media;
using GithubAccelerator.Core.Services;
using GithubAccelerator.UI.Services;

namespace GithubAccelerator.UI.Models;

/// <summary>
/// 「官网访问诊断」表格的一行（候选或基线）。
/// 只做展示映射，不包含任何写 Hosts / 写系统配置的逻辑。
/// </summary>
public sealed class OfficialSiteDiagnosticsRow
{
    private static readonly IBrush FasterBrush = new SolidColorBrush(Color.Parse("#4CAF50"));
    private static readonly IBrush SlowerBrush = new SolidColorBrush(Color.Parse("#FF5722"));
    private static readonly IBrush SimilarBrush = new SolidColorBrush(Color.Parse("#FFC107"));
    private static readonly IBrush NeutralBrush = new SolidColorBrush(Color.Parse("#9E9E9E"));
    private static readonly IBrush BaselineBackground = new SolidColorBrush(Color.Parse("#14FFC107"));
    private static readonly IBrush Transparent = Brushes.Transparent;

    public OfficialSiteDiagnosticsRow(IpDiagnosticsRow row)
    {
        Domain = row.Domain;
        Ip = row.Ip;
        Source = UiText.Plain(row.Source);
        IsBaseline = row.IsBaseline;
        MedianText = row.MedianMs.HasValue ? $"{row.MedianMs.Value} ms" : "—";
        AvailabilityText = UiText.Plain(row.AvailabilityText);
        ComparisonText = UiText.Plain(row.ComparisonText);
        Comparison = row.Comparison;
        IsAvailable = row.Available;
    }

    public string Domain { get; }
    public string Ip { get; }
    public string Source { get; }
    public bool IsBaseline { get; }
    public bool IsCandidate => !IsBaseline;
    public string MedianText { get; }
    public string AvailabilityText { get; }
    public string ComparisonText { get; }
    public bool IsAvailable { get; }
    public BaselineComparison Comparison { get; }

    public string RowTag => IsBaseline ? "当前直连" : "备选 IP";

    public IBrush RowBackground => IsBaseline ? BaselineBackground : Transparent;

    public IBrush ComparisonBrush => Comparison switch
    {
        BaselineComparison.Faster => FasterBrush,
        BaselineComparison.Slower => SlowerBrush,
        BaselineComparison.Similar => SimilarBrush,
        _ => NeutralBrush
    };

    public IBrush AvailabilityBrush => IsAvailable ? FasterBrush : SlowerBrush;
}
