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
                ["SurfaceBackground"] = "#FF323232",
                ["SubtleBackground"] = "#FF3B3B3B",
                ["PrimaryText"] = "#FFF5F5F5",
                ["SecondaryText"] = "#FFCCCCCC",
                ["Accent"] = "#FF75C6FF",
                ["AccentText"] = "#FF071017",
                ["AccentSurface"] = "#FF243D4D",
                ["Border"] = "#FF3D3D3D",
                ["Icon"] = "#FFF5F5F5",
                ["Disabled"] = "#FF808080",
                ["Shadow"] = "#FF000000",
                ["PageBackground"] = "#FF272727",
                ["CardBackground"] = "#FF323232",
                ["SidebarBackground"] = "#FF202020",
                ["AccentInk"] = "#FF95D1FF",
                ["SwitchTrack"] = "#FF454545",
                ["NavigationSelected"] = "#FF2D2D2D"
            }
            : new Dictionary<string, string>
            {
                ["WindowBackground"] = "#FFFFFFFF",
                ["SurfaceBackground"] = "#FFFBFBFB",
                ["SubtleBackground"] = "#FFF5F5F5",
                ["PrimaryText"] = "#FF0A0A0B",
                ["SecondaryText"] = "#FF606060",
                ["Accent"] = "#FF75C6FF",
                ["AccentText"] = "#FF0A0A0B",
                ["AccentSurface"] = "#FFDDF1FF",
                ["Border"] = "#FFE5E5E5",
                ["Icon"] = "#FF25282B",
                ["Disabled"] = "#FFADB5BC",
                ["Shadow"] = "#FF000000",
                ["PageBackground"] = "#FFF3F3F3",
                ["CardBackground"] = "#FFFBFBFB",
                ["SidebarBackground"] = "#FFF3F3F3",
                ["AccentInk"] = "#FF29688F",
                ["SwitchTrack"] = "#FFFFFFFF",
                ["NavigationSelected"] = "#FFE7E7E7"
            };

        foreach (var (key, value) in colors)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
            brush.Freeze();
            resources[key] = brush;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_started) SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }
}
