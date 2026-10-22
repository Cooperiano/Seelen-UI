using System.IO.MemoryMappedFiles;
using System.IO;

namespace MenuBar.Services;

public sealed record PublisherPerformanceSnapshot(int ProducerProcessId, uint DemandMask, long SampleCount,
    long ConfigurationCount, double LastMilliseconds, double RecentP95Milliseconds, double MaximumMilliseconds);

public static class PublisherDiagnostics
{
    public const string MapName = "Local\\Stats.Performance.v1";
    internal const uint Magic = 0x31504653;
    internal const int Capacity = 576;
    internal const int SampleCapacity = 64;

    public static PublisherPerformanceSnapshot Read(string mapName = MapName)
    {
        using var mapping = MemoryMappedFile.OpenExisting(mapName, MemoryMappedFileRights.Read);
        using var view = mapping.CreateViewAccessor(0, Capacity, MemoryMappedFileAccess.Read);
        for (var attempt = 0; attempt < 32; ++attempt)
        {
            var sequence = view.ReadInt64(8);
            if (sequence == 0 || (sequence & 1) != 0) continue;
            Thread.MemoryBarrier();
            var magic = view.ReadUInt32(0);
            var version = view.ReadUInt32(4);
            var processId = view.ReadInt32(16);
            var mask = view.ReadUInt32(20);
            var count = view.ReadInt64(24);
            var configs = view.ReadInt64(32);
            var last = view.ReadDouble(40);
            var maximum = view.ReadDouble(48);
            var sampleLength = (int)Math.Clamp(count, 0, SampleCapacity);
            var samples = new double[sampleLength];
            for (var index = 0; index < sampleLength; ++index) samples[index] = view.ReadDouble(64 + index * sizeof(double));
            Thread.MemoryBarrier();
            if (view.ReadInt64(8) != sequence) continue;
            if (magic != Magic || version != 1 || count < 0 || processId <= 0)
                throw new InvalidDataException("Unsupported publisher diagnostics.");
            Array.Sort(samples);
            var p95 = samples.Length == 0 ? double.NaN : samples[(int)Math.Ceiling(samples.Length * .95) - 1];
            return new(processId, mask, count, configs, last, p95, maximum);
        }
        throw new IOException("Publisher diagnostic snapshot is busy or has no completed capture.");
    }
}

internal sealed class PublisherPerformanceWriter : IDisposable
{
    private readonly MemoryMappedFile _mapping;
    private readonly MemoryMappedViewAccessor _view;
    private long _count;
    private double _maximum;

    internal PublisherPerformanceWriter(string mapName)
    {
        _mapping = MemoryMappedFile.CreateOrOpen(mapName, PublisherDiagnostics.Capacity, MemoryMappedFileAccess.ReadWrite);
        try { _view = _mapping.CreateViewAccessor(0, PublisherDiagnostics.Capacity, MemoryMappedFileAccess.Write); }
        catch { _mapping.Dispose(); throw; }
        // Invalidate an old producer's record retained by a reader after restart.
        _view.Write(8, 0L);
    }

    internal void Record(MetricsCollectorDemand demand, long configurations, double milliseconds)
    {
        var sequence = (++_count) * 2;
        _view.Write(8, sequence - 1);
        Thread.MemoryBarrier();
        _view.Write(0, PublisherDiagnostics.Magic);
        _view.Write(4, 1U);
        _view.Write(16, Environment.ProcessId);
        uint mask = (demand.Platform ? 1U : 0) | (demand.NvidiaExtended ? 2U : 0) | (demand.Bluetooth ? 4U : 0)
            | (demand.MemoryDetails ? 8U : 0) | (demand.Processes ? 16U : 0) | (demand.Sensors ? 32U : 0);
        _view.Write(20, mask);
        _view.Write(24, _count);
        _view.Write(32, configurations);
        _view.Write(40, milliseconds);
        _view.Write(48, _maximum = Math.Max(_maximum, milliseconds));
        _view.Write(64 + (int)((_count - 1) % PublisherDiagnostics.SampleCapacity) * sizeof(double), milliseconds);
        Thread.MemoryBarrier();
        _view.Write(8, sequence);
    }

    public void Dispose() { _view.Dispose(); _mapping.Dispose(); }
}
