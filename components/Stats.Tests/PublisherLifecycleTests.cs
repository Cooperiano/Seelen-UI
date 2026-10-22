using System.IO.MemoryMappedFiles;
using HardwareTelemetry.Models;
using HardwareTelemetry.Services;
using MenuBar.Services;
using Monitoring.Contracts;

internal static class PublisherLifecycleTests
{
    public static void Run(Func<double, HardwareMetrics> sample)
    {
        var directory = Path.Combine(Path.GetTempPath(), "Stats-publisher-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new StatsSettingsStore(Path.Combine(directory, "settings.json"), Path.Combine(directory, "legacy.json"));
            store.SaveSettings(new(StatsMetricCatalog.Defaults(), RefreshIntervalMilliseconds: 500));
            var name = "Local\\Stats.Fixture." + Guid.NewGuid().ToString("N");
            var hub = new LiveMetricsHub();
            using var reader = new BlockingReader(sample);
            using var publisher = new MetricsPublisher(reader, store, hub, name, name + ".Legacy", name + ".Performance");
            Check(reader.Entered.Wait(3000), "Initial capture did not start.");
            using var cpu = hub.BeginModuleSession(MetricIds.Cpu);
            for (var index = 0; index < 500; ++index)
            {
                using var transient = hub.BeginModuleSession(MetricIds.Ram);
            }
            Check(!reader.ReadProcessMetrics && !reader.ReadMemoryDetails && reader.SetterDuringCapture == 0,
                "UI demand changes mutated the active capture reader.");
            reader.Release.Set();
            WaitUntil(() => reader.Count >= 2 && reader.ReadProcessMetrics, "Coalesced follow-up did not apply CPU demand.");
            Check(reader.MaximumConcurrent == 1 && reader.SetterDuringCapture == 0, "Reader captures or configuration overlapped.");
            using var map = MemoryMappedFile.OpenExisting(name, MemoryMappedFileRights.Read);
            using var view = map.CreateViewAccessor(0, SharedMetricsLayout.Capacity, MemoryMappedFileAccess.Read);
            using var legacyMap = MemoryMappedFile.OpenExisting(name + ".Legacy", MemoryMappedFileRights.Read);
            using var legacyView = legacyMap.CreateViewAccessor(0, SharedMetricsLayout.Capacity, MemoryMappedFileAccess.Read);
            WaitUntil(() => view.ReadInt64(8) >= 4 && (view.ReadInt64(8) & 1) == 0, "Shared snapshot did not commit.");
            Check(view.ReadUInt32(0) == SharedMetricsLayout.Magic && view.ReadUInt32(4) == SharedMetricsLayout.Version,
                "Shared v7 protocol changed.");
            Check(view.ReadDouble(SharedMetricsLayout.SensorOffset) == 65 && legacyView.ReadDouble(SharedMetricsLayout.SensorOffset) == 8,
                "Temperature headline or legacy power semantics changed.");
            Check(view.ReadUInt32(SharedMetricsLayout.EnabledMaskOffset) == 15 &&
                view.ReadUInt32(SharedMetricsLayout.OrderCountOffset) == StatsMetricCatalog.All.Count,
                "Prepared metric configuration lost enabled/order fields.");
            for (var index = 0; index < StatsMetricCatalog.All.Count; ++index)
            {
                Check(view.ReadUInt32(SharedMetricsLayout.OrderOffset + index * 4) == index &&
                    view.ReadByte(SharedMetricsLayout.StyleOffset + index) == MetricDisplayStyles.ToProtocolValue(MetricDisplayStyles.Stacked) &&
                    view.ReadUInt32(SharedMetricsLayout.ColorOffset + index * 4) == MetricColors.ToProtocolArgb(StatsMetricCatalog.All[index].Accent),
                    "Prepared order/style/color is incompatible.");
            }
            WaitUntil(() => ReadPerformance(name).SampleCount >= 2, "Capture diagnostics did not commit.");
            var perf = ReadPerformance(name);
            Check(perf.ConfigurationCount == 1 && perf.SampleCount >= 2 && perf.ProducerProcessId == Environment.ProcessId,
                "Configuration was rebuilt on every capture or diagnostic identity is wrong.");
            Check(hub.GetHistory(MetricIds.Cpu).Count >= 2 && hub.GetHistory(MetricIds.Power).Count >= 2,
                "Aggregate history stopped after demand changes.");
            cpu.Dispose();
            WaitUntil(() => !reader.ReadProcessMetrics, "Closing the popup did not release process demand.");
            var raised = 0;
            publisher.AlertRaised += _ => Interlocked.Increment(ref raised);
            var changed = store.LoadSettings() with
            {
                Metrics = StatsMetricCatalog.All.Select(item => new StatsMetricPreference(item.Id, false, MetricDisplayStyles.Inline, "#FF123456")).ToArray(),
                Alerts = [new(MetricIds.GpuTemperature, true, 0, SustainSeconds: 0)]
            };
            store.SaveSettings(changed);
            WaitUntil(() => reader.ReadNvidiaExtendedMetrics && !reader.ReadSensorMetrics, "Settings reload or alert-only demand failed.");
            WaitUntil(() => view.ReadUInt32(SharedMetricsLayout.EnabledMaskOffset) == 0, "Updated configuration was not published.");
            Check(view.ReadByte(SharedMetricsLayout.StyleOffset) == 1 && view.ReadUInt32(SharedMetricsLayout.ColorOffset) == 0xFF123456,
                "Settings style/color did not invalidate the prepared configuration.");
            WaitUntil(() => ReadPerformance(name).ConfigurationCount == 2 && Volatile.Read(ref raised) == 1,
                "Configuration cache rebuild or alert-only notification failed.");
            reader.BlockNext();
            using var gpu = hub.BeginModuleSession(MetricIds.Gpu);
            Check(reader.Entered.Wait(3000), "Blocked disposal fixture did not enter capture.");
            var disposing = Task.Run(publisher.Dispose);
            Check(!disposing.Wait(100) && !reader.IsDisposed, "Dispose closed resources before its running callback drained.");
            reader.Release.Set();
            Check(disposing.Wait(3000) && reader.IsDisposed, "Dispose did not finish after capture drained.");
            var stoppedSequence = view.ReadInt64(8);
            using (hub.BeginModuleSession(MetricIds.Ram)) { }
            Thread.Sleep(550);
            Check(view.ReadInt64(8) == stoppedSequence, "Capture continued after disposal.");
            publisher.Dispose();
            Console.WriteLine("Passed publisher fixtures: 1,000 interest events, serial reader flags, v7/legacy compatibility, cached configuration, history, reload and in-flight disposal.");

            var diagnosticName = name + ".Ring";
            using var ringReader = MemoryMappedFile.CreateOrOpen(diagnosticName, PublisherDiagnostics.Capacity);
            using (var writer = new PublisherPerformanceWriter(diagnosticName))
            {
                for (var index = 1; index <= 70; ++index) writer.Record(default, 1, index);
                var ring = PublisherDiagnostics.Read(diagnosticName);
                Check(ring.SampleCount == 70 && ring.LastMilliseconds == 70 && ring.MaximumMilliseconds == 70 && ring.RecentP95Milliseconds == 67,
                    "Bounded diagnostic ring wrap or percentile is wrong.");
            }
            using (var restarted = new PublisherPerformanceWriter(diagnosticName))
            {
                try { _ = PublisherDiagnostics.Read(diagnosticName); throw new InvalidOperationException("Stale producer diagnostics remained visible."); }
                catch (IOException) { }
                restarted.Record(default, 1, 5);
                Check(PublisherDiagnostics.Read(diagnosticName).SampleCount == 1, "Diagnostic producer restart did not reset history.");
            }
            Console.WriteLine("Passed bounded performance diagnostics and retained-map restart fixtures.");
        }
        finally
        {
            // Only the exact private fixture directory created above, never live settings.
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void WaitUntil(Func<bool> condition, string message)
    {
        if (!SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(3))) throw new InvalidOperationException(message);
    }
    private static PublisherPerformanceSnapshot ReadPerformance(string name)
    {
        try { return PublisherDiagnostics.Read(name + ".Performance"); }
        catch (IOException) { return new(0, 0, 0, 0, double.NaN, double.NaN, double.NaN); }
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private sealed class BlockingReader(Func<double, HardwareMetrics> sample) : IHardwareMetricsReader, IDisposable
    {
        private readonly bool[] _flags = new bool[6];
        private int _active;
        private int _block = 1;
        public ManualResetEventSlim Entered { get; } = new(false);
        public ManualResetEventSlim Release { get; } = new(false);
        public int Count;
        public int MaximumConcurrent;
        public int SetterDuringCapture;
        public bool IsDisposed;
        private void Set(int index, bool value)
        {
            if (Volatile.Read(ref _active) > 0) Interlocked.Increment(ref SetterDuringCapture);
            Volatile.Write(ref _flags[index], value);
        }
        public bool ReadPlatformMetrics { get => Volatile.Read(ref _flags[0]); set => Set(0, value); }
        public bool ReadNvidiaExtendedMetrics { get => Volatile.Read(ref _flags[1]); set => Set(1, value); }
        public bool ReadBluetoothMetrics { get => Volatile.Read(ref _flags[2]); set => Set(2, value); }
        public bool ReadMemoryDetails { get => Volatile.Read(ref _flags[3]); set => Set(3, value); }
        public bool ReadProcessMetrics { get => Volatile.Read(ref _flags[4]); set => Set(4, value); }
        public bool ReadSensorMetrics { get => Volatile.Read(ref _flags[5]); set => Set(5, value); }
        public HardwareMetrics Capture()
        {
            if (IsDisposed) throw new InvalidOperationException("Disposed reader was captured.");
            var active = Interlocked.Increment(ref _active);
            MaximumConcurrent = Math.Max(MaximumConcurrent, active);
            try
            {
                if (Interlocked.Exchange(ref _block, 0) != 0)
                {
                    Entered.Set();
                    if (!Release.Wait(3000)) throw new InvalidOperationException("Private capture fixture timed out.");
                }
                return sample(Interlocked.Increment(ref Count));
            }
            finally { Interlocked.Decrement(ref _active); }
        }
        public void BlockNext() { Entered.Reset(); Release.Reset(); Volatile.Write(ref _block, 1); }
        public void Dispose() { IsDisposed = true; Release.Set(); }
    }
}
