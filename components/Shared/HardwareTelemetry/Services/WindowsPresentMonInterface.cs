using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using HardwareTelemetry.Models;

namespace HardwareTelemetry.Services;

// Opt-in bounded diagnostics only, NOT a new background collector. No installer, service, elevation or group changes.
public static class WindowsPresentMonInterface
{
    public const string Version = "2.6.0-x64";
    public const string OfficialSha256 = "B2A706BC6AD475749E3B7E3409263AA1E6906D45BDCF993F6DBC0F660188F1AF";

    public static ProcessStartInfo CreateStartInfo(string executable, int seconds, uint? processId = null)
    {
        if (seconds is < 1 or > 30) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (processId == 0) throw new ArgumentOutOfRangeException(nameof(processId));
        var info = new ProcessStartInfo(Path.GetFullPath(executable))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in new[] { "--timed", seconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--terminate_after_timed", "--no_console_stats", "--no_track_input", "--no_track_gpu", "--v1_metrics",
            "--output_stdout", "--session_name", "StatsReadonlyFPS_" + Guid.NewGuid().ToString("N") })
            info.ArgumentList.Add(argument);
        if (processId is { } pid) { info.ArgumentList.Add("--process_id"); info.ArgumentList.Add(pid.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
        return info;
    }

    public static async Task<FrameRateCapture> CaptureAsync(string executable, int seconds = 5,
        uint? processId = null, CancellationToken cancellationToken = default)
    {
        var startInfo = CreateStartInfo(executable, seconds, processId);
        FrameRateCapture Result(SensorInterfaceStatus status, string detail, int? error = null) =>
            new([], new("Intel PresentMon", "ETW presented events", status, detail, error), Version, seconds);
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
            return Result(SensorInterfaceStatus.Unsupported, "The pinned helper requires 64-bit Windows.");
        try
        {
            // Pin exact audited asInvoker binary; never execute an arbitrary helper or an unreviewed version.
            using var binary = new FileStream(startInfo.FileName, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (Convert.ToHexString(await SHA256.HashDataAsync(binary, cancellationToken)) != OfficialSha256)
                return Result(SensorInterfaceStatus.InvalidPayload, "Helper SHA-256 does not match the audited official asset; not executed.");
            using var process = new Process { StartInfo = startInfo };
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(seconds + 10));
            try
            {
                if (!process.Start()) return Result(SensorInterfaceStatus.Failed, "Could not start the verified helper.");
                var stdout = ReadBoundedAsync(process.StandardOutput, 4 * 1024 * 1024, deadline.Token);
                var stderr = ReadBoundedAsync(process.StandardError, 32 * 1024, deadline.Token);
                await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(deadline.Token));
                if (process.ExitCode != 0)
                    return new([], DescribeFailure(process.ExitCode, await stderr), Version, seconds);
                var csv = await stdout;
                IReadOnlyList<PresentedFrameRate> streams = string.IsNullOrWhiteSpace(csv) ? [] : PresentMonCsvParser.Parse(csv);
                return new(streams, new("Intel PresentMon", "ETW presented events", SensorInterfaceStatus.Readable,
                    streams.Count > 0 ? "Per-PID / swap-chain presented event rate; not displayed/application-generated FPS."
                        : "Capture succeeded without frame events. No synthetic zero FPS."), Version, seconds);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { return Result(SensorInterfaceStatus.Failed, "Bounded helper capture timed out; no frame result retained."); }
            finally
            {
                // Abnormal termination can leave ETW running. Clean only our freshly generated session name.
                // Normal completion uses PresentMon's own StopTrace. Never enumerate/stop other sessions or processes.
                var killed = false;
                try { if (process.Id != 0 && !process.HasExited) { process.Kill(); killed = true; } }
                catch (InvalidOperationException) { }
                if (killed)
                {
                    var session = startInfo.ArgumentList[startInfo.ArgumentList.IndexOf("--session_name") + 1];
                    await CleanupOwnedSessionAsync(startInfo.FileName, session);
                }
            }
        }
        catch (FileNotFoundException) { return Result(SensorInterfaceStatus.NoInterface, "Explicit helper path does not exist; nothing installed."); }
        catch (DirectoryNotFoundException) { return Result(SensorInterfaceStatus.NoInterface, "Explicit helper directory does not exist; nothing installed."); }
        catch (UnauthorizedAccessException) { return Result(SensorInterfaceStatus.PermissionDenied, "Helper file access denied."); }
        catch (InvalidDataException exception) { return Result(SensorInterfaceStatus.InvalidPayload, exception.Message); }
        catch (Exception exception) when (exception is IOException or Win32Exception)
        { return Result(SensorInterfaceStatus.Failed, exception.Message); }
    }

    internal static ProcessStartInfo CreateCleanupStartInfo(string executable, string ownedSession)
    {
        const string prefix = "StatsReadonlyFPS_";
        if (!ownedSession.StartsWith(prefix, StringComparison.Ordinal) || ownedSession.Length != prefix.Length + 32 ||
            !Guid.TryParseExact(ownedSession[prefix.Length..], "N", out _))
            throw new ArgumentException("Cleanup accepts only this wrapper's unique session names.", nameof(ownedSession));
        var info = new ProcessStartInfo(executable)
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        // Upstream handles this command before privilege-adjustment/restart logic. No --restart_as_admin.
        info.ArgumentList.Add("--terminate_existing_session");
        info.ArgumentList.Add("--session_name");
        info.ArgumentList.Add(ownedSession);
        return info;
    }

    private static async Task CleanupOwnedSessionAsync(string executable, string ownedSession)
    {
        using var cleanup = new Process { StartInfo = CreateCleanupStartInfo(executable, ownedSession) };
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            if (!cleanup.Start()) throw new IOException("Could not clean up our interrupted ETW session.");
            var stdout = ReadBoundedAsync(cleanup.StandardOutput, 4096, deadline.Token);
            var stderr = ReadBoundedAsync(cleanup.StandardError, 4096, deadline.Token);
            await Task.WhenAll(stdout, stderr, cleanup.WaitForExitAsync(deadline.Token));
            if (cleanup.ExitCode != 0 && !(cleanup.ExitCode == 7 && (await stderr).Contains("error: no existing sessions found:", StringComparison.Ordinal)))
                throw new IOException("Own-session ETW cleanup failed: " + (await stderr).Trim());
        }
        catch (OperationCanceledException) { throw new IOException("Own-session ETW cleanup timed out; session cleanup is unverified."); }
        finally
        {
            try { if (cleanup.Id != 0 && !cleanup.HasExited) cleanup.Kill(); }
            catch (InvalidOperationException) { }
        }
    }

    internal static SensorInterfaceProbe DescribeFailure(int exitCode, string stderr)
    {
        // 6 is PresentMon's generic trace-start exit code, NOT Win32 ERROR_ACCESS_DENIED (5).
        var denied = exitCode == 6 && stderr.Contains("error: failed to start trace session: access denied.", StringComparison.Ordinal);
        return new("Intel PresentMon", "ETW presented events", denied ? SensorInterfaceStatus.PermissionDenied : SensorInterfaceStatus.Failed,
            "Helper exit " + exitCode + ": " + stderr.Trim(), denied ? 5 : null);
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, int limit, CancellationToken token)
    {
        var output = new StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token)) > 0)
        {
            if (output.Length + count > limit) throw new InvalidDataException("Helper output exceeded bounded capture limit.");
            output.Append(buffer, 0, count);
        }
        return output.ToString();
    }
}
