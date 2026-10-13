namespace BingLan.Core.Dock;

/// <summary>
/// Which window the dock should treat as the foreground one. The top bar's search
/// session temporarily takes the foreground so its box can receive text; the dock
/// keeps evaluating the window the session borrowed it from, so focusing the box
/// does not read as the desktop coming back and show a dock that was hidden for a
/// maximized window in front.
/// </summary>
public static class DockForegroundRules
{
    public static nint EffectiveForeground(nint foreground, nint searchHolder, nint borrowed) =>
        searchHolder != 0 && borrowed != 0 && borrowed != searchHolder && foreground == searchHolder
            ? borrowed
            : foreground;
}
