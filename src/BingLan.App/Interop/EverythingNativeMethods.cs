using System.Runtime.InteropServices;

namespace BingLan.App.Interop;

internal static class EverythingNativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct CopyData
    {
        internal nint Id;
        internal int Length;
        internal nint Data;
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", SetLastError = true)]
    internal static extern nint SendMessageTimeout(nint window, int message, nint wParam,
        ref CopyData data, int flags, uint timeout, out nint result);
}
