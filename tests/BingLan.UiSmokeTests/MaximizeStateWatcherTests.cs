using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using BingLan.App.Dock;
using BingLan.App.Interop;

/// <summary>
/// Drives the maximize state watcher's event filter directly: only top-level state
/// changes of currently zoomed windows may raise the notification, and it must
/// arrive on the dispatcher thread. (The hook itself skips our own process, so the
/// callback is invoked by hand with a real window handle.)
/// </summary>
internal static class MaximizeStateWatcherTests
{
    internal static void Run(Action<Window> show, Action pump, Action<Func<bool>, string> pumpUntil)
    {
        var watcher = new MaximizeStateWatcher(Dispatcher.CurrentDispatcher);
        var raised = 0;
        watcher.MaximizedStateChanged += () => raised++;
        watcher.Start();
        var host = new Window
        {
            Width = 300,
            Height = 200,
            ShowInTaskbar = false,
            ShowActivated = false
        };
        show(host);
        var hwnd = new WindowInteropHelper(host).Handle;
        try
        {
            // 非最大化窗口的状态变化应被过滤。
            watcher.OnWinEvent(0, DockNativeMethods.EventObjectStateChange, hwnd, 0, 0, 0, 0);
            pump();
            if (raised != 0)
            {
                throw new InvalidOperationException("非最大化窗口的状态变化不应触发通知");
            }

            host.WindowState = WindowState.Maximized;
            pump();
            // 子对象的状态变化（idObject 不是窗口）应被过滤。
            watcher.OnWinEvent(0, DockNativeMethods.EventObjectStateChange, hwnd, -4, 0, 0, 0);
            pump();
            if (raised != 0)
            {
                throw new InvalidOperationException("子对象的状态变化不应触发通知");
            }

            // 最大化后的顶层状态变化应立即在调度线程上触发通知。
            watcher.OnWinEvent(0, DockNativeMethods.EventObjectStateChange, hwnd, 0, 0, 0, 0);
            pumpUntil(() => raised == 1, "最大化窗口的状态变化应触发一次通知");
        }
        finally
        {
            watcher.Dispose();
            host.Close();
            pump();
        }
    }
}
