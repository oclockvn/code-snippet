using System.Globalization;
using System.Windows.Data;
using CodeSnippet.Services;

namespace CodeSnippet.Converters;

/// <summary>Renders a timestamp as a relative label. Pass ConverterParameter="Sentence" for the Manager's
/// lower-case "2 minutes ago" style; omit it for the popup's compact uppercase "2 MIN AGO" style.</summary>
public sealed class RelativeTimeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DateTimeOffset timestamp)
        {
            return "Never";
        }

        var sentenceCase = string.Equals(parameter as string, "Sentence", StringComparison.OrdinalIgnoreCase);
        return RelativeTimeFormatter.Format(timestamp, sentenceCase);
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
