using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace PhoneCompanion.Windows;

public enum AppTheme { Light, Dark }

public sealed class SystemThemeService : IDisposable
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private readonly ResourceDictionary _resources;
    private readonly Dispatcher _dispatcher;
    private bool _started;
    private bool _disposed;

    public SystemThemeService(ResourceDictionary resources, Dispatcher dispatcher)
    {
        _resources = resources ?? throw new ArgumentNullException(nameof(resources));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started) return;
        _started = true;
        ApplyPalette(_resources, ReadSystemTheme());
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_disposed || _dispatcher.HasShutdownStarted) return;
        _dispatcher.BeginInvoke(() =>
        {
            if (!_disposed) ApplyPalette(_resources, ReadSystemTheme());
        }, DispatcherPriority.Background);
    }

    public static AppTheme ReadSystemTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey, writable: false);
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0
                ? AppTheme.Dark
                : AppTheme.Light;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.IO.IOException)
        {
            return AppTheme.Light;
        }
    }

    public static void ApplyPalette(ResourceDictionary resources, AppTheme theme)
    {
        ArgumentNullException.ThrowIfNull(resources);
        IReadOnlyDictionary<string, string> colors = theme == AppTheme.Dark
            ? new Dictionary<string, string>
            {
                ["WindowBackground"] = "#FF202020",
                ["SurfaceBackground"] = "#FF2C2C2C",
                ["SubtleBackground"] = "#12FFFFFF",
                ["PrimaryText"] = "#FFFFFFFF",
                ["SecondaryText"] = "#FFC7C7C7",
                ["Border"] = "#18FFFFFF",
                ["CardStroke"] = "#20FFFFFF",
                ["ControlStroke"] = "#30FFFFFF",
                ["ControlFill"] = "#14FFFFFF",
                ["ControlHover"] = "#12FFFFFF",
                ["ControlPressed"] = "#1CFFFFFF",
                ["Icon"] = "#FFF2F2F2",
                ["Disabled"] = "#FF858585",
                ["Shadow"] = "#FF000000",
                ["PageBackground"] = "#FF1C1C1C",
                ["CardBackground"] = "#FF2B2B2B",
                ["SidebarBackground"] = "#FF202020",
                ["SwitchTrack"] = "#38FFFFFF",
                ["NavigationSelected"] = "#14FFFFFF"
            }
            : new Dictionary<string, string>
            {
                ["WindowBackground"] = "#FFF3F3F3",
                ["SurfaceBackground"] = "#FFF9F9F9",
                ["SubtleBackground"] = "#0A000000",
                ["PrimaryText"] = "#FF1A1A1A",
                ["SecondaryText"] = "#FF5D5D5D",
                ["Border"] = "#16000000",
                ["CardStroke"] = "#19000000",
                ["ControlStroke"] = "#26000000",
                ["ControlFill"] = "#B3FFFFFF",
                ["ControlHover"] = "#0F000000",
                ["ControlPressed"] = "#18000000",
                ["Icon"] = "#FF202020",
                ["Disabled"] = "#FF9A9A9A",
                ["Shadow"] = "#FF000000",
                ["PageBackground"] = "#FFF9F9F9",
                ["CardBackground"] = "#FFFFFFFF",
                ["SidebarBackground"] = "#FFF3F3F3",
                ["SwitchTrack"] = "#33000000",
                ["NavigationSelected"] = "#0F000000"
            };

        foreach (var (key, value) in colors)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
            brush.Freeze();
            resources[key] = brush;
        }

        var accent = SystemParameters.WindowGlassColor;
        if (accent.A < 96) accent = Color.FromRgb(0, 103, 192);
        accent.A = 255;
        SetBrush(resources, "Accent", accent);
        SetBrush(resources, "AccentText", RelativeLuminance(accent) > 0.48 ? Colors.Black : Colors.White);
        SetBrush(resources, "AccentInk", Blend(accent, theme == AppTheme.Dark ? Colors.White : Colors.Black,
            theme == AppTheme.Dark ? 0.28 : 0.12));
        SetBrush(resources, "AccentSurface", Color.FromArgb(theme == AppTheme.Dark ? (byte)54 : (byte)31,
            accent.R, accent.G, accent.B));
    }

    private static void SetBrush(ResourceDictionary resources, string key, Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        resources[key] = brush;
    }

    private static Color Blend(Color source, Color target, double amount) => Color.FromRgb(
        (byte)Math.Round(source.R + (target.R - source.R) * amount),
        (byte)Math.Round(source.G + (target.G - source.G) * amount),
        (byte)Math.Round(source.B + (target.B - source.B) * amount));

    private static double RelativeLuminance(Color color)
    {
        static double Channel(byte value)
        {
            var linear = value / 255d;
            return linear <= 0.03928 ? linear / 12.92 : Math.Pow((linear + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_started) SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }
}
