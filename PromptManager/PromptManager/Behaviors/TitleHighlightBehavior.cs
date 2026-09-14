using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using PromptManager.ViewModels;

namespace PromptManager.Behaviors;

/// <summary>
/// Renders <see cref="SearchResultRow.TitleSegments"/> (plain/matched runs, possibly non-contiguous
/// for a fuzzy match) as <see cref="Run"/> inlines on a <see cref="TextBlock"/>. WPF can't bind
/// Inlines directly, so this attached property rebuilds them whenever the segment list changes.
/// </summary>
public static class TitleHighlightBehavior
{
    public static readonly DependencyProperty SegmentsProperty = DependencyProperty.RegisterAttached(
        "Segments",
        typeof(IReadOnlyList<TitleSegment>),
        typeof(TitleHighlightBehavior),
        new PropertyMetadata(null, OnSegmentsChanged));

    public static void SetSegments(TextBlock element, IReadOnlyList<TitleSegment>? value) => element.SetValue(SegmentsProperty, value);

    public static IReadOnlyList<TitleSegment>? GetSegments(TextBlock element) => (IReadOnlyList<TitleSegment>?)element.GetValue(SegmentsProperty);

    private static void OnSegmentsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var textBlock = (TextBlock)d;
        textBlock.Inlines.Clear();

        if (e.NewValue is not IReadOnlyList<TitleSegment> segments)
        {
            return;
        }

        foreach (var segment in segments)
        {
            var run = new Run(segment.Text);
            if (segment.IsMatch)
            {
                run.Background = MatchHighlightBrush;
                run.SetResourceReference(Run.ForegroundProperty, "Accent400");
            }

            textBlock.Inlines.Add(run);
        }
    }

    private static readonly System.Windows.Media.Brush MatchHighlightBrush =
        (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#38FF9783")!;
}
