using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MultiClod.App.Costs;

/// <summary>
/// AND's "does this badge have any cost text at all" with the app-wide
/// CostDisplaySettings.Instance.ShowCosts flag - both must be true for the text to show. Used only
/// inside CostBadge's own template, bound as a two-value MultiBinding (CostText, ShowCosts) rather
/// than computed in each view model's own Has*/*Text properties, so toggling the setting doesn't
/// require every already-materialized instance (SessionNodeViewModel, SessionLogSourceViewModel,
/// and especially the many TranscriptRowViewModel rows in a long transcript) to individually
/// observe and re-raise PropertyChanged.
/// </summary>
public sealed class CostVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Length == 2 && values[0] is string { Length: > 0 } && values[1] is true
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
