using System.Globalization;
using System.Windows.Data;

namespace SevenSeas.Launcher.Converters;

/// <summary>Inverts a boolean for XAML bindings (for example IsBusy -> IsEnabled).</summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool flag ? !flag : true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool flag ? !flag : false;
}
