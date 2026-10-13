using System.Windows.Threading;
using BingLan.App.Interop;

namespace BingLan.App.Dock;

/// <summary>
/// Immediate notice that a top-level window became (or stays) maximized, via the
/// accessibility event stream — the 150ms foreground poll cannot react to the
/// maximize click itself, which reads as a pause before the dock collapses. Only
/// state changes of windows that are currently zoomed pass the filter; everything
/// else keeps waiting for the poll. The event is raised on the dispatcher the
/// watcher was started on.
/// </summary>
internal sealed class MaximizeStateWatcher : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly DockNativeMethods.WinEventProc _callback;
    private readonly List<nint> _hooks = [];
    private bool _disposed;

    internal MaximizeStateWatcher(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _callback = OnWinEvent;
    }

    /// <summary>Raised on the dispatcher thread whenever a zoomed window changes state.</summary>
    internal event Action? MaximizedStateChanged;

    internal void Start()
    {
        Hook(DockNativeMethods.EventObjectStateChange, DockNativeMethods.EventObjectStateChange);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var hook in _hooks)
        {
            DockNativeMethods.UnhookWinEvent(hook);
        }
        _hooks.Clear();
    }

    private void Hook(uint eventMin, uint eventMax)
    {
        var hook = DockNativeMethods.SetWinEventHook(
            eventMin,
            eventMax,
            0,
            _callback,
            0,
            0,
            DockNativeMethods.WineventOutOfContext | DockNativeMethods.WineventSkipOwnProcess);
        if (hook != 0)
        {
            _hooks.Add(hook);
        }
    }

    // Out-of-context: the callback arrives on this thread's message loop, which the
    // WPF dispatcher already pumps.
    internal void OnWinEvent(
        nint hook,
        uint eventType,
        nint window,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime)
    {
        if (_disposed
            || window == 0
            || objectId != DockNativeMethods.ObjidWindow
            || childId != 0
            || !DockNativeMethods.IsZoomed(window))
        {
            return;
        }

        _dispatcher.BeginInvoke(DispatcherPriority.Normal, () => MaximizedStateChanged?.Invoke());
    }
}
