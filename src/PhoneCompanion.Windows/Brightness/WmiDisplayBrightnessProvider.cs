using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;

namespace PhoneCompanion.Windows.Brightness;

internal sealed class WmiDisplayBrightnessProvider : IDisposable
{
    private readonly ManagementObject _brightness;
    private readonly ManagementObject _methods;
    private readonly byte[] _levels;
    private bool _disposed;

    private WmiDisplayBrightnessProvider(
        ManagementObject brightness,
        ManagementObject methods,
        byte[] levels)
    {
        _brightness = brightness;
        _methods = methods;
        _levels = levels;
    }

    public static WmiDisplayBrightnessProvider? TryCreate()
    {
        ManagementObject? brightness = null;
        ManagementObject? methods = null;
        try
        {
            brightness = FindActive("SELECT * FROM WmiMonitorBrightness WHERE Active = TRUE");
            if (brightness is null) return null;

            var instance = brightness["InstanceName"] as string;
            methods = FindActive("SELECT * FROM WmiMonitorBrightnessMethods WHERE Active = TRUE", instance);
            if (methods is null) return null;

            var levels = (brightness["Level"] as byte[])?
                .Distinct()
                .OrderBy(value => value)
                .ToArray();
            if (levels is null || levels.Length == 0) return null;

            var provider = new WmiDisplayBrightnessProvider(brightness, methods, levels);
            brightness = null;
            methods = null;
            return provider;
        }
        catch
        {
            return null;
        }
        finally
        {
            brightness?.Dispose();
            methods?.Dispose();
        }
    }

    public int? ReadCurrentLevel()
    {
        ThrowIfDisposed();
        _brightness.Get();
        return _brightness["CurrentBrightness"] is byte level ? level : null;
    }

    public int SetLevel(int requestedLevel)
    {
        ThrowIfDisposed();
        var nearest = _levels.MinBy(level => Math.Abs(level - requestedLevel));
        _methods.InvokeMethod("WmiSetBrightness", new object[] { 0u, nearest });
        return nearest;
    }

    public void RestorePolicy()
    {
        ThrowIfDisposed();
        _methods.InvokeMethod("WmiRevertToPolicyBrightness", Array.Empty<object>());
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _brightness.Dispose();
        _methods.Dispose();
    }

    private static ManagementObject? FindActive(string query, string? requiredInstance = null)
    {
        using var searcher = new ManagementObjectSearcher(@"root\wmi", query);
        using var results = searcher.Get();
        foreach (ManagementObject candidate in results)
        {
            var instance = candidate["InstanceName"] as string;
            if (requiredInstance is not null && !string.Equals(instance, requiredInstance, StringComparison.OrdinalIgnoreCase))
            {
                candidate.Dispose();
                continue;
            }

            return candidate;
        }

        return null;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
