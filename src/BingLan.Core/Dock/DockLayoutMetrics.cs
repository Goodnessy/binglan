namespace BingLan.Core.Dock;

public static class DockLayoutMetrics
{
    public const double DockThicknessDip = 68;
    public const double EdgeGapDip = 28;
    public const double AxisPaddingDip = 14;
    public const double ItemCellDip = 64;
    public const double EmptyLengthDip = 76;
    public const double MaxExtentFraction = 0.65;

    private const double DefaultIconDip = 40;

    /// <summary>Width of the divider between pinned apps and other running apps.</summary>
    public const double DividerDip = 13;

    /// <summary>The space one app takes along the dock: its button plus the gap beside it.</summary>
    public static double ItemCell(double iconSize) => ItemCellDip - DefaultIconDip + iconSize;

    /// <summary>The dock's height for an icon size.</summary>
    public static double Thickness(double iconSize) => DockThicknessDip - DefaultIconDip + iconSize;

    public static double ContentLengthForItems(int itemCount, double iconSize = DefaultIconDip)
    {
        if (itemCount <= 0)
        {
            return EmptyLengthDip;
        }

        return (2 * AxisPaddingDip) + (itemCount * ItemCell(iconSize));
    }
}
