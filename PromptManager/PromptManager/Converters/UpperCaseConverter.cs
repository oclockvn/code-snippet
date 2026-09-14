using System.Globalization;
using System.Windows.Data;

namespace CodeSnippet.Converters;

/// <summary>Renders text upper-case, standing in for the mock's CSS text-transform:uppercase on tag/label chrome.</summary>
public sealed class UpperCaseConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value as string)?.ToUpper(culture) ?? string.Empty;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
