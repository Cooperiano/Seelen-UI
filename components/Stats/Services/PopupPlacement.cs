namespace MenuBar.Services;

internal static class PopupPlacement
{
    private const int EdgeMargin = 8;
    private const int AnchorGap = 6;

    public static (int Left, int Top) Calculate(
        ScreenRect anchor,
        ScreenRect workArea,
        int windowWidth,
        int windowHeight)
    {
        var minimumLeft = workArea.Left + EdgeMargin;
        var maximumLeft = Math.Max(minimumLeft, workArea.Right - windowWidth - EdgeMargin);
        var left = Math.Clamp(anchor.CenterX - windowWidth / 2, minimumLeft, maximumLeft);

        var below = anchor.Bottom + AnchorGap;
        var above = anchor.Top - AnchorGap - windowHeight;
        var top = below + windowHeight <= workArea.Bottom - EdgeMargin
            ? below
            : Math.Max(workArea.Top + EdgeMargin, above);
        return (left, top);
    }
}
