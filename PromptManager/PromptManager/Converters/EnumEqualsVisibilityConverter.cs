using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PromptManager.Converters;

/// <summary>Visible when the bound enum value's name matches the ConverterParameter string; drives the popup's per-state panels.</summary>
public sealed class EnumEqualsVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && string.Equals(value.ToString(), parameter as string, StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
