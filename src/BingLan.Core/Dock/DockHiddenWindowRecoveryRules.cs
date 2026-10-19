using BingLan.Core.Models;

namespace BingLan.Core.Dock;

/// <summary>
/// Conservatively identifies a hidden top-level app window that may be restored
/// when a pinned app was closed to the notification area. A running process alone
/// is not sufficient: background services must not be made visible by accident.
/// </summary>
public static class DockHiddenWindowRecoveryRules
{
    public static bool CanRecover(
        DockPinnedApp pinned,
        TrackedWindow window,
        bool isVisible,
        bool hasOwner,
        bool isToolWindow,
        bool isCloaked,
        bool hasAppWindowStyle)
    {
        ArgumentNullException.ThrowIfNull(pinned);
        ArgumentNullException.ThrowIfNull(window);

        if (window.Handle == 0
            || window.ProcessId == 0
            || isVisible
            || hasOwner
            || isToolWindow
            || isCloaked
            || (!hasAppWindowStyle && string.IsNullOrWhiteSpace(window.Title)))
        {
            return false;
        }

        // Preserve the existing strict AUMID matching rules. Never revive another
        // web app merely because it shares a browser executable.
        var group = new WindowGroup(WindowGrouping.IdentityKey(window), window.Title, [window]);
        return DockPinRules.Matches(pinned, group);
    }
}
