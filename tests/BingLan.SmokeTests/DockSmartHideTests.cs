using BingLan.Core.Dock;

namespace BingLan.SmokeTests;

/// <summary>
/// The timing of smart-hide decisions: ordinary overlaps idle through the debounce
/// delay, while a maximized window covering the dock hides it at once.
/// </summary>
internal static class DockSmartHideTests
{
    internal static void Run()
    {
        var now = DateTimeOffset.UtcNow;

        // 普通窗口遮挡：延迟内保持显示，到期才隐藏。
        var ordinary = new DockAutoHideInput(
            WindowOverlapsDock: true,
            ForegroundIsFullScreen: false,
            PointerInRevealZone: false,
            PointerOverDock: false,
            InteractionActive: false,
            OverlapIsMaximizedWindow: false);
        var gradual = new DockAutoHideState();
        if (gradual.Update(ordinary, now) || gradual.Update(ordinary, now.AddMilliseconds(599)))
        {
            throw new InvalidOperationException("普通遮挡在隐藏延迟到期前不应隐藏");
        }
        if (!gradual.Update(ordinary, now.Add(DockAutoHideState.HideDelay)))
        {
            throw new InvalidOperationException("普通遮挡在隐藏延迟到期后应隐藏");
        }
        if (gradual.IsShown)
        {
            throw new InvalidOperationException("隐藏后 IsShown 应为 false");
        }

        // 最大化窗口遮挡：立即隐藏，不等延迟。
        var maximized = ordinary with { OverlapIsMaximizedWindow = true };
        var immediate = new DockAutoHideState();
        if (!immediate.Update(maximized, now))
        {
            throw new InvalidOperationException("最大化窗口遮挡应立即隐藏");
        }

        // 指针还在 Dock 上时优先保持显示；指针离开后同样立即隐藏。
        var pointerHeld = new DockAutoHideState();
        if (pointerHeld.Update(maximized with { PointerOverDock = true }, now))
        {
            throw new InvalidOperationException("指针在 Dock 上时不应隐藏");
        }
        if (!pointerHeld.Update(maximized, now.AddMilliseconds(1)))
        {
            throw new InvalidOperationException("指针离开后最大化遮挡应立即隐藏");
        }

        // 遮挡消失立即重新显示（反最大化恢复）。
        if (!pointerHeld.Update(ordinary with { WindowOverlapsDock = false }, now.AddMilliseconds(2)))
        {
            throw new InvalidOperationException("遮挡消失后应重新显示");
        }
    }
}
