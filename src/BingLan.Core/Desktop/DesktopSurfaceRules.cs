namespace BingLan.Core.Desktop;

/// <summary>What the card windows should do about their z-order position.</summary>
public enum DesktopSurfaceAction
{
    /// <summary>Nothing changed that concerns the cards.</summary>
    None,

    /// <summary>Lift the cards into the topmost band, under the taskbar.</summary>
    Raise,

    /// <summary>Put the cards back below every ordinary window.</summary>
    Lower
}

/// <summary>
/// Rules that keep the desktop cards visible while the shell shows the desktop.
/// Pressing "show desktop" makes Explorer minimise every ordinary window and raise
/// the desktop surface above the non-topmost band, which covers the cards that sit
/// at the bottom of that band behind the wallpaper. Only the topmost band is left
/// alone, so while that state lasts the cards are lifted into it — under the taskbar
/// so the taskbar stays reachable — and put back once ordinary windows return.
/// </summary>
public static class DesktopSurfaceRules
{
    /// <summary>
    /// Whether the desktop surface currently covers the cards: the shell has raised
    /// it and no visible, un-minimised ordinary window is left above it. Reading the
    /// state from the other windows (not from the cards' own position) means lifting
    /// the cards cannot flip the verdict.
    /// </summary>
    public static bool IsDesktopShown(bool desktopSurfaceFound, int ordinaryWindowsAboveSurface) =>
        desktopSurfaceFound && ordinaryWindowsAboveSurface == 0;

    /// <summary>
    /// Whether a window above the surface counts as ordinary. Windows of the shell's
    /// own process are its surface windows (wallpaper, taskbars) — except the
    /// file-manager windows, which run in explorer.exe too but are ordinary windows:
    /// one being open means the desktop is not shown, and the cards must go below it.
    /// </summary>
    public static bool CountsAsOrdinaryWindow(bool shellProcess, string className) =>
        !shellProcess || className is "CabinetWClass" or "ExploreWClass";

    /// <summary>What the cards should do now, given whether they were already lifted.</summary>
    public static DesktopSurfaceAction ActionFor(bool cardsRaised, bool desktopShown) =>
        (cardsRaised, desktopShown) switch
        {
            (false, true) => DesktopSurfaceAction.Raise,
            (true, false) => DesktopSurfaceAction.Lower,
            _ => DesktopSurfaceAction.None
        };
}
