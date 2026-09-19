using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace PhoneCompanion.Windows.Connection;

public sealed class PhoneScreenLauncher
{
    public string? Open()
    {
        var executable = FindScrcpy();
        if (executable is null)
            return "Open phone needs scrcpy. Install scrcpy or set UNITY_CONNECT_SCRCPY to scrcpy.exe.";
        try
        {
            var start = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            start.ArgumentList.Add("--window-title");
            start.ArgumentList.Add("Phone");
            start.ArgumentList.Add("--stay-awake");
            Process.Start(start);
            return null;
        }
        catch
        {
            return "The phone screen could not be opened. Check scrcpy and Android debugging access.";
        }
    }

    private static string? FindScrcpy()
    {
        var configured = Environment.GetEnvironmentVariable("UNITY_CONNECT_SCRCPY");
        foreach (var candidate in Candidates(configured))
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            var expanded = Environment.ExpandEnvironmentVariables(candidate);
            if (Directory.Exists(expanded)) expanded = Path.Combine(expanded, "scrcpy.exe");
            if (File.Exists(expanded)) return Path.GetFullPath(expanded);
        }
        return null;
    }

    private static IEnumerable<string?> Candidates(string? configured)
    {
        yield return configured;
        yield return Path.Combine(AppContext.BaseDirectory, "scrcpy.exe");
        yield return Path.Combine(AppContext.BaseDirectory, "scrcpy", "scrcpy.exe");
        yield return Path.Combine(AppContext.BaseDirectory, "tools", "scrcpy", "scrcpy.exe");
        yield return Path.Combine(Environment.CurrentDirectory, "scrcpy", "scrcpy.exe");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "scrcpy", "scrcpy.exe");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "scrcpy", "scrcpy.exe");
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            yield return Path.Combine(directory.Trim('"'), "scrcpy.exe");
    }
}
