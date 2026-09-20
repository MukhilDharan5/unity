using System;
using System.Runtime.InteropServices;

namespace PhoneCompanion.Windows.Security;

internal static class WindowsWorkstationLock
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LockWorkStation();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    public static bool TryLock() => LockWorkStation();

    public static TimeSpan GetIdleTime()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;
        var elapsed = unchecked((uint)Environment.TickCount - info.Time);
        return TimeSpan.FromMilliseconds(elapsed);
    }
}
