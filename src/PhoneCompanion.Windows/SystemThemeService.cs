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
                ["WindowBackground"] = "#FF090B0E",
                ["SurfaceBackground"] = "#FF111A21",
                ["SubtleBackground"] = "#FF15181C",
                ["PrimaryText"] = "#FFF8FAFC",
                ["SecondaryText"] = "#FFAAB4BD",
                ["Accent"] = "#FF75C6FF",
                ["AccentText"] = "#FF071017",
                ["AccentSurface"] = "#FF17364B",
                ["Border"] = "#FF293640",
                ["Icon"] = "#FFE9F2F8",
                ["Disabled"] = "#FF58636C",
                ["Shadow"] = "#FF000000"
            }
            : new Dictionary<string, string>
            {
                ["WindowBackground"] = "#FFFFFFFF",
                ["SurfaceBackground"] = "#FFF3F9FE",
                ["SubtleBackground"] = "#FFF6F7F8",
                ["PrimaryText"] = "#FF0A0A0B",
                ["SecondaryText"] = "#FF5D6268",
                ["Accent"] = "#FF75C6FF",
                ["AccentText"] = "#FF0A0A0B",
                ["AccentSurface"] = "#FFDDF1FF",
                ["Border"] = "#FFDCE5EC",
                ["Icon"] = "#FF25282B",
                ["Disabled"] = "#FFADB5BC",
                ["Shadow"] = "#FF000000"
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
