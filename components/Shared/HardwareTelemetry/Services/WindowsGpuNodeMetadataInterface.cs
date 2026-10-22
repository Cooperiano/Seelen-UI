using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using HardwareTelemetry.Models;

namespace HardwareTelemetry.Services;

// Diagnostic-only fixed KMTQAITYPE_NODEMETADATA query. Node ordinals come from
// the current WDDM capture, never a guessed scan or reserved QueryStatistics.
public static class WindowsGpuNodeMetadataInterface
{
    internal const int PayloadSize = 78; // SDK 26100 d3dkmdt.h, pack(1).
    private static readonly string[] EngineNames =
        ["Other", "3D", "VideoDecode", "VideoEncode", "VideoProcessing", "SceneAssembly", "Copy", "Overlay", "Crypto", "VideoCodec"];
    private static readonly Encoding StrictUnicode = new UnicodeEncoding(false, false, true);

    public static GpuNodeMetadataCapture Capture(GpuEngineCapture engines)
    {
        var nodes = new List<GpuNodeMetadata>();
        var probes = new List<SensorInterfaceProbe>();
        if (!OperatingSystem.IsWindows() || nint.Size != 8)
            return new([], [new("D3DKMT", "node-metadata", SensorInterfaceStatus.NoInterface, "This audited ABI is Windows x64 only.")]);
        foreach (var adapter in engines.Adapters.Where(adapter => !adapter.Software).DistinctBy(adapter => adapter.Luid).Take(32))
        {
            var candidates = engines.Engines.Where(node => node.AdapterLuid == adapter.Luid)
                .DistinctBy(node => (node.PhysicalAdapter, node.Engine)).Take(256).ToList();
            if (candidates.Count == 0) continue;
            var endpoint = adapter.Luid;
            if (!TryParseLuid(adapter.Luid, out var low, out var high))
            {
                probes.Add(new("D3DKMT", endpoint, SensorInterfaceStatus.InvalidPayload, "Malformed DXGI LUID; no adapter query attempted."));
                continue;
            }
            var opened = new OpenAdapter { LuidLow = low, LuidHigh = high };
            var ownsHandle = false;
            try
            {
                var status = D3DKMTOpenAdapterFromLuid(ref opened);
                if (status != 0) { probes.Add(Failure(endpoint + "/open", status)); continue; }
                if (opened.Handle == 0)
                {
                    probes.Add(new("D3DKMT", endpoint + "/open", SensorInterfaceStatus.InvalidPayload, "Successful open returned a null adapter handle."));
                    continue;
                }
                ownsHandle = true;
                foreach (var candidate in candidates)
                {
                    var nodeEndpoint = endpoint + $"/physical/{candidate.PhysicalAdapter}/node/{candidate.Engine}";
                    if (!IsValidCandidate(candidate))
                    {
                        probes.Add(new("D3DKMT", nodeEndpoint, SensorInterfaceStatus.InvalidPayload, "Counter node identity/value out of bounds; no metadata query attempted."));
                        continue;
                    }
                    var bytes = new byte[PayloadSize];
                    BinaryPrimitives.WriteUInt32LittleEndian(bytes, EncodeOrdinal(candidate));
                    var buffer = Marshal.AllocHGlobal(PayloadSize);
                    try
                    {
                        Marshal.Copy(bytes, 0, buffer, bytes.Length);
                        var query = new QueryAdapterInfo { Handle = opened.Handle, Type = 25,
                            Data = buffer, DataSize = PayloadSize };
                        status = D3DKMTQueryAdapterInfo(ref query);
                        if (status != 0) { probes.Add(Failure(nodeEndpoint, status)); continue; }
                        Marshal.Copy(buffer, bytes, 0, bytes.Length);
                        try
                        {
                            nodes.Add(Parse(candidate, bytes));
                            probes.Add(new("D3DKMT", nodeEndpoint, SensorInterfaceStatus.Readable,
                                "Driver node metadata read; utilization is the separately captured WDDM counter, not a new compute measurement."));
                        }
                        catch (InvalidDataException exception)
                        {
                            probes.Add(new("D3DKMT", nodeEndpoint, SensorInterfaceStatus.InvalidPayload, exception.Message));
                        }
                    }
                    finally { Marshal.FreeHGlobal(buffer); }
                }
            }
            catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
            {
                probes.Add(new("D3DKMT", endpoint, SensorInterfaceStatus.NoInterface, exception.Message));
            }
            finally
            {
                if (ownsHandle)
                {
                    try
                    {
                        var close = new CloseAdapter { Handle = opened.Handle };
                        var closeStatus = D3DKMTCloseAdapter(ref close);
                        if (closeStatus != 0) probes.Add(Failure(endpoint + "/close", closeStatus));
                    }
                    catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
                    {
                        probes.Add(new("D3DKMT", endpoint + "/close", SensorInterfaceStatus.NoInterface, exception.Message));
                    }
                }
            }
        }
        if (probes.Count == 0) probes.Add(new("D3DKMT", "node-metadata", SensorInterfaceStatus.NoInterface,
            "No current hardware adapter with published WDDM node identities; no guessed node enumeration attempted."));
        return new(nodes, probes);
    }

    internal static bool IsValidCandidate(GpuEngineTelemetry candidate) =>
        candidate.PhysicalAdapter is >= 0 and < 64 && candidate.Engine is >= 0 and < 256 &&
        double.IsFinite(candidate.UtilizationPercent) && candidate.UtilizationPercent is >= 0 and <= 100;
    internal static uint EncodeOrdinal(GpuEngineTelemetry candidate) =>
        (uint)candidate.Engine | ((uint)candidate.PhysicalAdapter << 16);

    internal static GpuNodeMetadata Parse(GpuEngineTelemetry candidate, ReadOnlySpan<byte> payload)
    {
        if (!IsValidCandidate(candidate) || payload.Length != PayloadSize ||
            BinaryPrimitives.ReadUInt32LittleEndian(payload) != EncodeOrdinal(candidate))
            throw new InvalidDataException("Node metadata length or requested physical/node identity mismatch.");
        var type = BinaryPrimitives.ReadUInt32LittleEndian(payload[4..]);
        if (type >= EngineNames.Length) throw new InvalidDataException("Unknown native engine type; no category inferred.");
        var nameBytes = payload.Slice(8, 64);
        var nameLength = 0;
        for (; nameLength < nameBytes.Length; nameLength += 2)
            if (nameBytes[nameLength] == 0 && nameBytes[nameLength + 1] == 0) break;
        if (nameLength == nameBytes.Length) throw new InvalidDataException("Driver friendly name is not terminated within the fixed SDK field.");
        string name;
        try { name = StrictUnicode.GetString(nameBytes[..nameLength]); }
        catch (DecoderFallbackException exception) { throw new InvalidDataException("Malformed UTF-16 driver name.", exception); }
        if (payload[76] > 1 || payload[77] > 1) throw new InvalidDataException("Invalid native BOOLEAN payload.");
        return new(candidate.AdapterLuid, candidate.PhysicalAdapter, candidate.Engine, candidate.EngineType,
            type, EngineNames[type], name, BinaryPrimitives.ReadUInt32LittleEndian(payload[72..]),
            payload[76] != 0, payload[77] != 0, candidate.UtilizationPercent);
    }

    internal static bool TryParseLuid(string luid, out uint low, out int high)
    {
        low = 0; high = 0;
        if (luid.Length != 17 || luid[8] != ':' ||
            !uint.TryParse(luid.AsSpan(0, 8), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var highBits) ||
            !uint.TryParse(luid.AsSpan(9, 8), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out low)) return false;
        high = unchecked((int)highBits);
        return true;
    }

    internal static SensorInterfaceProbe Failure(string endpoint, int status) => new("D3DKMT", endpoint,
        status == unchecked((int)0xC0000022) ? SensorInterfaceStatus.PermissionDenied :
        status == unchecked((int)0xC00000BB) ? SensorInterfaceStatus.Unsupported : SensorInterfaceStatus.Failed,
        $"Native NTSTATUS=0x{unchecked((uint)status):X8}; no metadata or synthetic reading produced.", status);

    [StructLayout(LayoutKind.Sequential)]
    internal struct OpenAdapter { public uint LuidLow; public int LuidHigh; public uint Handle; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct CloseAdapter { public uint Handle; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct QueryAdapterInfo { public uint Handle, Type; public nint Data; public uint DataSize; }
    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern int D3DKMTOpenAdapterFromLuid(ref OpenAdapter adapter);
    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern int D3DKMTQueryAdapterInfo(ref QueryAdapterInfo query);
    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern int D3DKMTCloseAdapter(ref CloseAdapter adapter);
}
