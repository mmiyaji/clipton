using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Clipton.WinUI;

internal sealed class SnippetVariableWrapPanel : Panel
{
    private const double Spacing = 6;

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
        {
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        }

        return LayoutChildren(availableSize.Width, arrange: false);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        LayoutChildren(finalSize.Width, arrange: true);
        return finalSize;
    }

    private Size LayoutChildren(double availableWidth, bool arrange)
    {
        var rowWidth = 0d;
        var rowHeight = 0d;
        var top = 0d;
        var usedWidth = 0d;

        foreach (var child in Children)
        {
            var width = Math.Min(child.DesiredSize.Width, availableWidth);
            var height = child.DesiredSize.Height;
            var left = rowWidth > 0 ? rowWidth + Spacing : 0;
            if (left > 0 && left + width > availableWidth)
            {
                left = 0;
                top += rowHeight + Spacing;
                rowHeight = 0;
            }

            if (arrange)
            {
                child.Arrange(new Rect(left, top, width, height));
            }

            rowWidth = left + width;
            rowHeight = Math.Max(rowHeight, height);
            usedWidth = Math.Max(usedWidth, rowWidth);
        }

        return new Size(usedWidth, top + rowHeight);
    }
}
