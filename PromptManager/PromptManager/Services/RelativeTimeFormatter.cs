using System.Globalization;

namespace CodeSnippet.Services;

/// <summary>Shared "2 MIN AGO" / "2 minutes ago" style relative-time text, used by the popup meta labels,
/// the Manager's stats row, and <see cref="Converters.RelativeTimeConverter"/>.</summary>
public static class RelativeTimeFormatter
{
    public static string Format(DateTimeOffset timestamp, bool sentenceCase)
    {
        var elapsed = DateTimeOffset.Now - timestamp;

        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return sentenceCase ? "just now" : "JUST NOW";
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            var minutes = (int)elapsed.TotalMinutes;
            return sentenceCase ? $"{minutes} minute{(minutes == 1 ? "" : "s")} ago" : $"{minutes} MIN AGO";
        }

        if (elapsed < TimeSpan.FromHours(24))
        {
            var hours = (int)elapsed.TotalHours;
            return sentenceCase ? $"{hours} hour{(hours == 1 ? "" : "s")} ago" : $"{hours} H AGO";
        }

        if (elapsed < TimeSpan.FromHours(48))
        {
            return sentenceCase ? "yesterday" : "YESTERDAY";
        }

        if (elapsed < TimeSpan.FromDays(7))
        {
            var days = (int)elapsed.TotalDays;
            return sentenceCase ? $"{days} days ago" : $"{days} DAYS AGO";
        }

        return sentenceCase
            ? timestamp.ToString("d MMM yyyy", CultureInfo.CurrentCulture)
            : timestamp.ToString("d MMM yyyy", CultureInfo.CurrentCulture).ToUpperInvariant();
    }
}
