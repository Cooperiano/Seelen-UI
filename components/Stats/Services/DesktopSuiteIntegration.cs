using System.Diagnostics;
using System.IO;

namespace MenuBar.Services;

internal enum DesktopSuiteModule { MenuBar, Touchpad, Stats }

// Only known existing product entry points. No preference copying, new
// collector, registry edits, startup mutations, or arbitrary shell command.
internal static class DesktopSuiteIntegration
{
    internal const string TouchpadAppId = "50931ClmentGrennerat.ThreeFingersDragOnWindows_cvkce2k9t2r60!App";

    internal static string MenuBarPath(string localDirectory)
    {
        var pointer = Path.Combine(localDirectory, "Programs", "Stats Desktop", "suite-host.txt");
        if (!File.Exists(pointer)) return "";
        var path = File.ReadAllText(pointer).Trim();
        return Path.IsPathFullyQualified(path) && string.Equals(Path.GetFileName(path), "seelen-ui.exe", StringComparison.OrdinalIgnoreCase)
            ? path : "";
    }

    internal static bool IsAvailable(DesktopSuiteModule module, string statsDirectory,
        string localDirectory, Func<string, bool>? exists = null)
    {
        exists ??= File.Exists;
        return exists(module switch
        {
            DesktopSuiteModule.MenuBar or DesktopSuiteModule.Stats => MenuBarPath(localDirectory),
            DesktopSuiteModule.Touchpad => Path.Combine(localDirectory, "Microsoft", "WindowsApps", "ThreeFingerDragOnWindows.exe"),
            _ => throw new ArgumentOutOfRangeException(nameof(module))
        });
    }

    internal static ProcessStartInfo LaunchPlan(DesktopSuiteModule module,
        string localDirectory, string windowsDirectory) => module switch
    {
        DesktopSuiteModule.MenuBar => HostPlan(localDirectory),
        DesktopSuiteModule.Stats => HostPlan(localDirectory, "stats"),
        DesktopSuiteModule.Touchpad when MenuBarPath(localDirectory).Length > 0 => HostPlan(localDirectory, "touchpad"),
        DesktopSuiteModule.Touchpad => new(Path.Combine(windowsDirectory, "explorer.exe"))
        {
            UseShellExecute = true,
            Arguments = "shell:AppsFolder\\" + TouchpadAppId
        },
        _ => throw new ArgumentOutOfRangeException(nameof(module))
    };

    private static ProcessStartInfo HostPlan(string localDirectory, string? page = null) => new(MenuBarPath(localDirectory))
    {
        UseShellExecute = true,
        WorkingDirectory = Path.GetDirectoryName(MenuBarPath(localDirectory))!,
        Arguments = page is null ? "--settings" : "--settings-route " + page
    };

    internal static bool TryOpenManagedSettings()
    {
        try
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var host = MenuBarPath(local);
            if (host.Length == 0 || !File.Exists(host)) return false;
            Process.Start(HostPlan(local, "stats"));
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return false; // Keep standalone settings usable when the suite is unavailable.
        }
    }

    internal static void Open(DesktopSuiteModule module)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!IsAvailable(module, AppContext.BaseDirectory, local))
            throw new FileNotFoundException("此模块入口不可用，请先恢复或安装对应应用。");
        Process.Start(LaunchPlan(module, local,
            Environment.GetFolderPath(Environment.SpecialFolder.Windows)));
    }
}

internal static class SettingsOpenRequest
{
    internal static string? Parse(IReadOnlyList<string> args)
    {
        if (args.Contains("--exit", StringComparer.OrdinalIgnoreCase)) return "exit";
        for (var index = 0; index < args.Count; index++)
        {
            if (string.Equals(args[index], "--module", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Count)
                return $"module:{args[index + 1]}";
        }
        if (args.Contains("--suite", StringComparer.OrdinalIgnoreCase)) return "suite";
        return args.Contains("--settings", StringComparer.OrdinalIgnoreCase) ? "settings" : null;
    }
}
