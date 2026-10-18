using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using BingLan.App;
using BingLan.App.Dock;
using BingLan.App.Interop;
using BingLan.Core.Models;
using Border = System.Windows.Controls.Border;
using Brushes = System.Windows.Media.Brushes;
using Window = System.Windows.Window;

internal static class DockGlassSurfaceTests
{
    internal static void Run(Action<Window> show, Action pump, Action<Func<bool>, string> until)
    {
        var surface = new Border { CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1), Child = new TextBlock { Text = "玻璃形变验证", Foreground = Brushes.White } };
        surface.SetResourceReference(Border.BackgroundProperty, "DockSurfaceBrush");
        surface.SetResourceReference(Border.BorderBrushProperty, "DockSurfaceBorderBrush");
        var window = new Window { Width = 420, Height = 84, Left = 210, Top = 230,
            WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
            ShowInTaskbar = false, Content = surface };
        using var glass = new DockGlassSurface(window, surface, "DockSurfaceBrush", "DockSurfaceBorderBrush");
        var animator = new DockMorphAnimator(surface);
        animator.StateChanged += () => glass.TrackAnimation(animator.ActiveKind != DockMorphKind.None);
        try
        {
            show(window);
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) && !SystemParameters.HighContrast)
            {
                // This machine's transparency policy determines whether the real native path is available.
                using var settings = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                if (settings?.GetValue("EnableTransparency") is int enabled && enabled == 0) return;
                until(() => glass.IsActive, "Dock 原生毛玻璃没有建立");
                var native = glass.BackgroundHandle;
                Assert((NativeMethods.GetExStyleOf(native) & NativeMethods.WsExNoActivate) != 0, "玻璃不能激活或抢焦点");
                Assert((NativeMethods.GetExStyleOf(native) & NativeMethods.WsExTopmost) == 0, "普通内容窗口的玻璃不能自行置顶");
                var bounds = glass.BackgroundBounds;
                var hwnd = new WindowInteropHelper(window).Handle;
                var sizeMessages = 0;
                HwndSourceHook hook = (nint h, int m, nint w, nint l, ref bool handled) => { if (m == NativeMethods.WmSize) sizeMessages++; return 0; };
                HwndSource.FromHwnd(hwnd)!.AddHook(hook);
                animator.BeginCollapse(new System.Windows.Point(380, 60), MotionLevel.Standard);
                pump();
                animator.BeginExpand(new System.Windows.Point(380, 60), MotionLevel.Standard);
                until(() => animator.ActiveKind == DockMorphKind.None, "玻璃反向形变没有收敛");
                Assert(glass.BackgroundBounds == bounds && sizeMessages == 0, "形变不能逐帧改变窗口尺寸");
                HwndSource.FromHwnd(hwnd)!.RemoveHook(hook);
                window.Left += 55;
                pump();
                Assert(glass.BackgroundBounds?.Left != bounds?.Left, "玻璃必须跟随窗口移动");
                window.Width += 40;
                pump();
                Assert(glass.BackgroundBounds?.Width > bounds?.Width, "内容尺寸变化后玻璃必须同步");
                AccessibilityThemeManager.SetHighContrastForTesting(true);
                pump();
                Assert(!glass.IsActive && !NativeMethods.IsWindowVisible(native), "高对比度必须移除玻璃窗口");
                Assert(surface.Background == SystemColors.WindowBrush, "高对比度必须恢复系统背景");
                AccessibilityThemeManager.RestoreSystemModeForTesting();
                pump();
                until(() => glass.IsActive, "退出高对比度后玻璃没有恢复");
                native = glass.BackgroundHandle;
                window.Hide();
                pump();
                Assert(!glass.IsActive && !NativeMethods.IsWindowVisible(native), "隐藏不能遗留玻璃");
                show(window);
                until(() => glass.IsActive, "重新显示必须恢复玻璃");
                native = glass.BackgroundHandle;
                window.Close();
                pump();
                Assert(!NativeMethods.IsWindowVisible(native), "关闭必须销毁玻璃");
            }
        }
        finally
        {
            if (window.IsLoaded) window.Close();
            AccessibilityThemeManager.RestoreSystemModeForTesting();
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
