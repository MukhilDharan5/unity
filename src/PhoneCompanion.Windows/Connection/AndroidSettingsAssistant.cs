using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PhoneCompanion.Windows.Connection;

/// <summary>Optional ADB assistance for user-visible Android settings. ADB is never a companion transport.</summary>
public sealed class AndroidSettingsAssistant
{
    public bool TryOpenPhoneBluetoothSettings() => TryOpenPhoneSettings("android.settings.BLUETOOTH_SETTINGS");

    public bool TryOpenPhoneHotspotSettings() => TryOpenPhoneSettings(
        "android.settings.TETHER_SETTINGS", "android.settings.WIRELESS_SETTINGS");

    private static bool TryOpenPhoneSettings(params string[] actions)
    {
        var adb = FindAdb();
        if (adb is null) return false;
        try
        {
            var devices = Run(adb, "devices");
            if (devices.ExitCode != 0) return false;
            var authorized = devices.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Split('\t', 2))
                .Where(parts => parts.Length == 2 && parts[1].Trim().Equals("device", StringComparison.Ordinal))
                .Select(parts => parts[0].Trim())
                .Where(serial => serial.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (authorized.Length != 1) return false;
            foreach (var action in actions)
            {
                var launched = Run(adb, "-s", authorized[0], "shell", "am", "start", "-a", action);
                if (launched.ExitCode == 0 &&
                    !launched.Output.Contains("Error:", StringComparison.OrdinalIgnoreCase) &&
                    !launched.Output.Contains("Exception", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
        catch { return false; }
    }

    public bool OpenWindowsBluetoothSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:bluetooth") { UseShellExecute = true });
            return true;
        }
        catch { return false; }
    }

    public bool OpenWindowsWifiSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:network-wifi") { UseShellExecute = true });
            return true;
        }
        catch { return false; }
    }

    private static (int ExitCode, string Output) Run(string executable, params string[] arguments)
    {
        var start = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("ADB could not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(3500))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return (-1, string.Empty);
        }
        Task.WaitAll(new Task[] { output, error }, 1000);
        return (process.ExitCode, output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
    }

    private static string? FindAdb()
    {
        foreach (var candidate in Candidates())
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            var expanded = Environment.ExpandEnvironmentVariables(candidate);
            if (Directory.Exists(expanded)) expanded = Path.Combine(expanded, "adb.exe");
            if (File.Exists(expanded)) return Path.GetFullPath(expanded);
        }
        return null;
    }

    private static IEnumerable<string?> Candidates()
    {
        yield return Environment.GetEnvironmentVariable("UNITY_CONNECT_ADB");
        yield return Path.Combine(AppContext.BaseDirectory, "adb.exe");
        yield return Path.Combine(AppContext.BaseDirectory, "platform-tools", "adb.exe");
        yield return Path.Combine(AppContext.BaseDirectory, "tools", "adb", "adb.exe");
        yield return Path.Combine(AppContext.BaseDirectory, "scrcpy", "adb.exe");
        var scrcpy = Environment.GetEnvironmentVariable("UNITY_CONNECT_SCRCPY");
        if (!string.IsNullOrWhiteSpace(scrcpy))
        {
            var expanded = Environment.ExpandEnvironmentVariables(scrcpy);
            yield return Path.Combine(Directory.Exists(expanded) ? expanded : Path.GetDirectoryName(expanded) ?? string.Empty,
                "adb.exe");
        }
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Android", "Sdk", "platform-tools", "adb.exe");
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            yield return Path.Combine(directory.Trim('"'), "adb.exe");
    }
}
