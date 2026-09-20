using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PhoneCompanion.Windows;

internal static class WindowsWindowStyling
{
    private const int UseImmersiveDarkMode = 20;
    private const int WindowCornerPreference = 33;
    private const int CaptionColor = 35;
    private const int TextColor = 36;
    private const int SystemBackdropType = 38;

    public static void Apply(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;

        var dark = SystemThemeService.ReadSystemTheme() == AppTheme.Dark ? 1 : 0;
        DwmSetWindowAttribute(handle, UseImmersiveDarkMode, ref dark, sizeof(int));

        var rounded = 2;
        DwmSetWindowAttribute(handle, WindowCornerPreference, ref rounded, sizeof(int));

        var backdrop = 2;
        DwmSetWindowAttribute(handle, SystemBackdropType, ref backdrop, sizeof(int));

        var caption = dark == 1 ? ColorRef(32, 32, 32) : ColorRef(243, 243, 243);
        DwmSetWindowAttribute(handle, CaptionColor, ref caption, sizeof(int));
        var text = dark == 1 ? ColorRef(255, 255, 255) : ColorRef(26, 26, 26);
        DwmSetWindowAttribute(handle, TextColor, ref text, sizeof(int));
    }

    private static int ColorRef(byte red, byte green, byte blue) => red | green << 8 | blue << 16;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
}
