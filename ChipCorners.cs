using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DynamicIsland;

public sealed class ChipCorners : IValueConverter
{
    const double MaxRadius = 14;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        new CornerRadius(Math.Min(MaxRadius, (double)value / 2));

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
