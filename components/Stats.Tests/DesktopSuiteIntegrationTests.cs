using MenuBar.Services;

internal static class DesktopSuiteIntegrationTests
{
    public static void Run()
    {
        Check(SettingsOpenRequest.Parse([]) is null, "no implicit UI request");
        Check(SettingsOpenRequest.Parse(["--settings"]) == "settings", "legacy settings route");
        Check(SettingsOpenRequest.Parse(["--SUITE"]) == "suite", "suite route case-insensitive");
        Check(SettingsOpenRequest.Parse(["--suite", "--settings"]) == "suite", "suite wins over legacy route");
        Check(SettingsOpenRequest.Parse(["--suite", "--module", "cpu"]) == "module:cpu", "metric details remain explicit");
        Check(SettingsOpenRequest.Parse(["--suite", "--exit"]) == "exit", "exit does not open hub");
        Check(SettingsOpenRequest.Parse(["--module"]) is null, "missing module value safe");
        var root = Path.Combine(Path.GetTempPath(), "suite fixture space");
        var stats = Path.Combine(root, "Stats", "publish");
        var bar = Path.Combine(root, "host", "seelen-ui.exe");
        var local = Path.Combine(root, "user");
        var pointer = Path.Combine(local, "Programs", "Stats Desktop", "suite-host.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(pointer)!);
        File.WriteAllText(pointer, bar);
        Check(DesktopSuiteIntegration.MenuBarPath(local) == bar, "registered suite host path");
        var plan = DesktopSuiteIntegration.LaunchPlan(DesktopSuiteModule.MenuBar, local, "C:\\Windows");
        Check(plan.FileName == bar && plan.WorkingDirectory == Path.GetDirectoryName(bar) && plan.Arguments == "--settings", "only explicit central settings argument");
        plan = DesktopSuiteIntegration.LaunchPlan(DesktopSuiteModule.Touchpad, local, "C:\\Windows");
        Check(plan.FileName == bar && plan.Arguments == "--settings-route touchpad", "touchpad settings use in-window route");
        plan = DesktopSuiteIntegration.LaunchPlan(DesktopSuiteModule.Stats, local, "C:\\Windows");
        Check(plan.FileName == bar && plan.Arguments == "--settings-route stats", "Stats settings use in-window route");
        plan = DesktopSuiteIntegration.LaunchPlan(DesktopSuiteModule.Touchpad, Path.Combine(root, "standalone"), "C:\\Windows");
        Check(plan.FileName == "C:\\Windows\\explorer.exe" && plan.Arguments == "shell:AppsFolder\\" + DesktopSuiteIntegration.TouchpadAppId, "touchpad uses stable registered identity");
        Check(!DesktopSuiteIntegration.IsAvailable(DesktopSuiteModule.MenuBar, stats, local, _ => false), "missing bar unavailable");
        Check(!DesktopSuiteIntegration.IsAvailable(DesktopSuiteModule.Touchpad, stats, local, _ => false), "missing touchpad unavailable");
        Check(DesktopSuiteIntegration.IsAvailable(DesktopSuiteModule.MenuBar, stats, local, path => path == bar), "known bar exists");
        Check(DesktopSuiteIntegration.IsAvailable(DesktopSuiteModule.Touchpad, stats, local,
            path => path == Path.Combine(local, "Microsoft", "WindowsApps", "ThreeFingerDragOnWindows.exe")), "known alias exists");
        try { DesktopSuiteIntegration.LaunchPlan((DesktopSuiteModule)999, stats, "C:\\Windows"); throw new Exception("Unknown module accepted"); }
        catch (ArgumentOutOfRangeException) { }
        try { DesktopSuiteIntegration.IsAvailable((DesktopSuiteModule)999, stats, local, _ => true); throw new Exception("Unknown module accepted"); }
        catch (ArgumentOutOfRangeException) { }
        File.WriteAllText(pointer, Path.Combine(root, "Brimbar.exe"));
        Check(DesktopSuiteIntegration.MenuBarPath(local) == "", "reject old or arbitrary host executable");
        File.Delete(pointer);
        Check(DesktopSuiteIntegration.MenuBarPath(local) == "", "no implicit legacy Brimbar fallback");
        Console.WriteLine("Passed 20 desktop suite route/launch/availability checks (no application launched).");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
