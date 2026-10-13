using BingLan.Core.Desktop;

/// <summary>
/// Rules that decide when the cards must rise into the topmost band because the
/// shell is showing the desktop. The Win32 calls themselves are checked by hand.
/// </summary>
internal static class DesktopSurfaceTests
{
    internal static void TestShownStateAndActions()
    {
        Check(DesktopSurfaceRules.IsDesktopShown(true, 0), "桌面表面已抬高且无普通窗口时应判定为显示桌面");
        Check(!DesktopSurfaceRules.IsDesktopShown(true, 1), "仍有普通窗口在上时不应判定为显示桌面");
        Check(!DesktopSurfaceRules.IsDesktopShown(true, 7), "多个普通窗口在上时不应判定为显示桌面");
        Check(!DesktopSurfaceRules.IsDesktopShown(false, 0), "找不到桌面表面时不应判定为显示桌面");

        Check(DesktopSurfaceRules.ActionFor(false, true) == DesktopSurfaceAction.Raise,
            "未提升且桌面已显示时应提升");
        Check(DesktopSurfaceRules.ActionFor(true, true) == DesktopSurfaceAction.None,
            "已提升且桌面仍显示时应保持");
        Check(DesktopSurfaceRules.ActionFor(true, false) == DesktopSurfaceAction.Lower,
            "已提升且桌面不再显示时应回落");
        Check(DesktopSurfaceRules.ActionFor(false, false) == DesktopSurfaceAction.None,
            "未提升且桌面未显示时应不动");
    }

    internal static void TestOrdinaryWindowClassification()
    {
        Check(DesktopSurfaceRules.CountsAsOrdinaryWindow(false, "Chrome_WidgetWin_1"),
            "其他进程的窗口应算普通窗口");
        Check(DesktopSurfaceRules.CountsAsOrdinaryWindow(false, string.Empty),
            "取不到类名的其他进程窗口仍应算普通窗口");
        Check(DesktopSurfaceRules.CountsAsOrdinaryWindow(true, "CabinetWClass"),
            "资源管理器窗口属于外壳进程，仍应算普通窗口，打开后组件须落到它下面");
        Check(DesktopSurfaceRules.CountsAsOrdinaryWindow(true, "ExploreWClass"),
            "旧式资源管理器窗口也应算普通窗口");
        Check(!DesktopSurfaceRules.CountsAsOrdinaryWindow(true, "WorkerW"),
            "外壳壁纸窗口不应算普通窗口");
        Check(!DesktopSurfaceRules.CountsAsOrdinaryWindow(true, "Shell_TrayWnd"),
            "任务栏不应算普通窗口");
        Check(!DesktopSurfaceRules.CountsAsOrdinaryWindow(true, "Progman"),
            "桌面表面自身不应算普通窗口");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
