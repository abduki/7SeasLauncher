using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SevenSeas.Launcher.Converters;

/// <summary>Shows an element when the bound boolean is false.</summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool flag && flag ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility visibility && visibility != Visibility.Visible;
}
