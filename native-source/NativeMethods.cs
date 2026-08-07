using System.Runtime.InteropServices;

namespace CodexRelay;

internal static class NativeMethods
{
    private const uint FlashAll = 3;
    private const uint FlashTimerNoForeground = 12;
    private const int AttachParentProcess = -1;

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashWindowInfo
    {
        public uint Size;
        public IntPtr Window;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FlashWindowInfo info);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(int processId);

    public static void FlashWindow(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        var info = new FlashWindowInfo
        {
            Size = (uint)Marshal.SizeOf<FlashWindowInfo>(),
            Window = windowHandle,
            Flags = FlashAll | FlashTimerNoForeground,
            Count = 6,
            Timeout = 0
        };
        _ = FlashWindowEx(ref info);
    }

    public static void AttachToParentConsole() => _ = AttachConsole(AttachParentProcess);
}
