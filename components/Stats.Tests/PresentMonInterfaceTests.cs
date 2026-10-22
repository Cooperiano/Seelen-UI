using System.IO;
using HardwareTelemetry.Models;
using HardwareTelemetry.Services;

internal static class PresentMonInterfaceTests
{
    public static void Run()
    {
        const string header = "Application,ProcessID,SwapChainAddress,Dropped,TimeInSeconds\n";
        var values = PresentMonCsvParser.Parse("\uFEFF" + header +
            "\"game, one.exe\",12,0x1234,0,0\n" +
            "other.exe,13,0x1234,0,0\n" +
            "\"game, one.exe\",12,0x1235,0,0.25\n" +
            "\"game, one.exe\",12,0x1234,1,0.01\n" +
            "other.exe,13,0x1234,0,0.02\n" +
            "\"game, one.exe\",12,0x1234,0,0.02\n");
        if (values.Count != 3 || values[0] is not { Application: "game, one.exe", PresentCount: 3, DroppedCount: 1, PresentsPerSecond: 100 } ||
            values[1].PresentsPerSecond is not null || values[2].PresentsPerSecond != 50)
            throw new InvalidOperationException("Frame seconds, PID/swap-chain isolation, dropped events or single-frame unknown rate failed.");
        if (PresentMonCsvParser.Parse(header).Count != 0)
            throw new InvalidOperationException("Empty capture fabricated frames.");
        var unknownChain = PresentMonCsvParser.Parse(header + "game,1,0x0,0,0\ngame,1,0x0,0,1\n")[0];
        if (unknownChain.SwapChainIdentified || unknownChain.PresentsPerSecond is not null || unknownChain.PresentCount != 2)
            throw new InvalidOperationException("Unknown ETW swap-chain address was discarded or given an invented window rate.");
        var escaped = PresentMonCsvParser.Parse(header + "\"game \"\"one\"\".exe\",1,0x1,0,0\n");
        if (escaped[0].Application != "game \"one\".exe") throw new InvalidOperationException("CSV escaped quote failed.");
        foreach (var bad in new[] {
            "", header.Replace("TimeInSeconds", "CPUStartTime") + "game,1,0x1,0,16.7\n",
            header + "game,0,0x1,0,0\n", header + "game,1,0xZZ,0,0\n", header + "game,1,0x1,2,0\n",
            header + "game,1,0x1,0,NaN\n", header + "game,1,0x1,0,Infinity\n", header + "game,1,0x1,0,-1\n",
            header + "game,1,0x1,0,0\ngame,1,0x1,0,0\n", header + "game,1,0x1,0,1\ngame,1,0x1,0,0\n",
            header + "game,1,0x1,0,0\nrenamed,1,0x1,0,1\n", header + "\"bad,1,0x1,0,0\n",
            header + "\"bad\"suffix,1,0x1,0,0\n", header + "game,1,0x1,0,0,extra\n",
            "Application,ProcessID,SwapChainAddress,Dropped,TimeInSeconds,ProcessID\n" })
        {
            try { PresentMonCsvParser.Parse(bad); }
            catch (InvalidDataException) { continue; }
            throw new InvalidOperationException("Malformed/ambiguous frame CSV accepted.");
        }
        var info = WindowsPresentMonInterface.CreateStartInfo("C:/readonly/PresentMon.exe", 5, 12);
        var secondInfo = WindowsPresentMonInterface.CreateStartInfo("C:/readonly/PresentMon.exe", 5);
        if (info.UseShellExecute || !info.CreateNoWindow || !info.RedirectStandardOutput || !info.RedirectStandardError ||
            !info.ArgumentList.Contains("--v1_metrics") || !info.ArgumentList.Contains("--no_track_input") ||
            !info.ArgumentList.Contains("--no_track_gpu") || !info.ArgumentList.Contains("--process_id") ||
            info.ArgumentList.Contains("--restart_as_admin") || info.ArgumentList.Contains("--stop_existing_session") ||
            info.ArgumentList.Contains("--terminate_existing_session") ||
            info.ArgumentList[info.ArgumentList.IndexOf("--session_name") + 1] == secondInfo.ArgumentList[secondInfo.ArgumentList.IndexOf("--session_name") + 1])
            throw new InvalidOperationException("Optional helper launch isolation/safety contract failed.");
        foreach (var seconds in new[] { 0, 31 })
        {
            try { WindowsPresentMonInterface.CreateStartInfo("C:/readonly/PresentMon.exe", seconds); }
            catch (ArgumentOutOfRangeException) { continue; }
            throw new InvalidOperationException("Unbounded frame capture accepted.");
        }
        var ownSession = info.ArgumentList[info.ArgumentList.IndexOf("--session_name") + 1];
        var cleanupInfo = WindowsPresentMonInterface.CreateCleanupStartInfo(info.FileName, ownSession);
        if (cleanupInfo.UseShellExecute || cleanupInfo.ArgumentList.Contains("--restart_as_admin") ||
            cleanupInfo.ArgumentList.Count != 3 || cleanupInfo.ArgumentList[2] != ownSession)
            throw new InvalidOperationException("Owned-session cleanup expanded its target or elevation scope.");
        foreach (var unsafeName in new[] { "PresentMon", "NT Kernel Logger", "StatsReadonlyFPS_default", "StatsReadonlyFPS_" + new string('x', 32) })
        {
            try { WindowsPresentMonInterface.CreateCleanupStartInfo(info.FileName, unsafeName); }
            catch (ArgumentException) { continue; }
            throw new InvalidOperationException("Unrelated ETW session accepted as a cleanup target.");
        }
        if (WindowsPresentMonInterface.DescribeFailure(6, "error: failed to start trace session: access denied.").Status != SensorInterfaceStatus.PermissionDenied ||
            WindowsPresentMonInterface.DescribeFailure(6, "error: failed to start trace session: file not found.").Status != SensorInterfaceStatus.Failed ||
            WindowsPresentMonInterface.DescribeFailure(5, "hotkey registration failed").Status == SensorInterfaceStatus.PermissionDenied)
            throw new InvalidOperationException("Helper exit code was mistaken for a Win32 permission error.");
        Console.WriteLine("Passed PresentMon V1 CSV identity/unit/bounds and no-elevation launch checks.");
    }
}
