using System.Buffers.Binary;
using System.IO;
using System.Text;
using HardwareTelemetry.Services;
using HardwareTelemetry.Models;

internal static class NativeInterfaceParserTests
{
    public static void Run()
    {
        foreach (var pointerSize in new[] { 4, 8 })
        {
            var compression = new byte[pointerSize == 8 ? 40 : 24];
            BinaryPrimitives.WriteUInt32LittleEndian(compression, 3);
            BinaryPrimitives.WriteUInt32LittleEndian(compression.AsSpan(4), 3936);
            BinaryPrimitives.WriteUInt32LittleEndian(compression.AsSpan(8), 1_600_000_000);
            var offset = pointerSize == 8 ? 16 : 12;
            for (var counter = 0; counter < 3; counter++)
                if (pointerSize == 8) BinaryPrimitives.WriteUInt64LittleEndian(compression.AsSpan(offset + counter * pointerSize), 8_000_000_000UL);
                else BinaryPrimitives.WriteUInt32LittleEndian(compression.AsSpan(offset + counter * pointerSize), 2_000_000_000);
            var compressionValue = WindowsMemoryCompressionInterface.Parse(compression, pointerSize);
            if (compressionValue.ProcessId != 3936 || compressionValue.ResidentBytes != 1_600_000_000 || compressionValue.TotalCompressedSize != (pointerSize == 8 ? 8_000_000_000UL : 2_000_000_000UL))
                throw new InvalidOperationException("Compression native layout/byte units were misread.");
            Reject(() => WindowsMemoryCompressionInterface.Parse(compression[..^1], pointerSize));
            compression[0] = 4;
            Reject(() => WindowsMemoryCompressionInterface.Parse(compression, pointerSize));
            compression[0] = 3;
            Array.Clear(compression, 4, compression.Length - 4);
            if (WindowsMemoryCompressionInterface.Parse(compression, pointerSize).ResidentBytes != 0)
                throw new InvalidOperationException("Valid zero compression usage was discarded.");
        }
        if (System.Runtime.InteropServices.Marshal.SizeOf<WindowsMemoryCompressionInterface.StoreInformation>() != (nint.Size == 8 ? 24 : 16) ||
            WindowsMemoryCompressionInterface.DescribeStatus(unchecked((int)0xC0000061)) != SensorInterfaceStatus.PermissionDenied)
            throw new InvalidOperationException("Compression envelope layout or denied-status classification failed.");
        var gpuRows = new[]
        {
            GpuEngineParser.Parse("pid_1_luid_0x00000000_0x0000e586_phys_0_eng_0_engtype_3D", 30)!,
            GpuEngineParser.Parse("pid_2_luid_0x00000000_0x0000e586_phys_0_eng_0_engtype_3D", 40)!,
            GpuEngineParser.Parse("pid_3_luid_0x00000000_0x0000eef7_phys_0_eng_0_engtype_3D", 60)!,
            GpuEngineParser.Parse("pid_4_luid_0x00000000_0x0000eef7_phys_0_eng_1_engtype_Copy", 0)!
        };
        var gpuTotals = GpuEngineParser.Aggregate(gpuRows);
        if (gpuTotals.Count != 3 || gpuTotals[0].UtilizationPercent != 70 ||
            gpuTotals[1].UtilizationPercent != 60 || gpuTotals[2].UtilizationPercent != 0)
            throw new InvalidOperationException("GPU LUID isolation, per-process aggregation, or valid idle zero was lost.");
        foreach (var name in new[] { "_Total", "pid_1_phys_0_eng_0_engtype_3D", "pid_1_luid_x_y_phys_0_eng_0_engtype_3D" })
            if (GpuEngineParser.Parse(name, 0) is not null) throw new InvalidOperationException("An ambiguous GPU identity was accepted.");
        foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, -1d })
            if (GpuEngineParser.Parse("pid_1_luid_0x00000000_0x00000001_phys_0_eng_0_engtype_3D", invalid) is not null)
                throw new InvalidOperationException("Invalid GPU utilization was accepted.");
        if (System.Runtime.InteropServices.Marshal.SizeOf<WindowsGpuEngineInterface.AdapterDescription>() != (nint.Size == 8 ? 312 : 296) ||
            System.Runtime.InteropServices.Marshal.OffsetOf<WindowsGpuEngineInterface.AdapterDescription>("LuidLow").ToInt32() != (nint.Size == 8 ? 296 : 284))
            throw new InvalidOperationException("DXGI_ADAPTER_DESC1 layout mismatch.");
        if (!ProcessorFrequencyParser.TryParse("1,12", 2304, 150, out var processorFrequency) ||
            processorFrequency is not { Group: 1, Processor: 12, EstimatedMhz: 3456 })
            throw new InvalidOperationException("Processor group/frequency estimate was not preserved.");
        if (!ProcessorFrequencyParser.TryParse("0,0", 2304, 0, out var zeroFrequency) || zeroFrequency?.EstimatedMhz != 0)
            throw new InvalidOperationException("Valid zero processor performance was discarded.");
        foreach (var name in new[] { "_Total", "0,_Total", "-1,0", "0,64", "65536,0", "0", "0,0,0" })
            if (ProcessorFrequencyParser.TryParse(name, 2304, 100, out _))
                throw new InvalidOperationException("Invalid processor identity accepted: " + name);
        foreach (var sample in new[] { (double.NaN, 100d), (0d, 100d), (2304d, -1d),
            (2304d, double.PositiveInfinity), (2304d, 100_000d) })
            if (ProcessorFrequencyParser.TryParse("0,0", sample.Item1, sample.Item2, out _))
                throw new InvalidOperationException("Invalid processor estimate became a reading.");
        if (ReadOnlyNativeDevice.Failure("test", "short", new InvalidDataException("short buffer")).Status != SensorInterfaceStatus.InvalidPayload)
            throw new InvalidOperationException("Malformed native buffer was not classified explicitly.");
        var batteryStatus = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(batteryStatus.AsSpan(4), 90_000);
        BinaryPrimitives.WriteUInt32LittleEndian(batteryStatus.AsSpan(8), 15_000);
        BinaryPrimitives.WriteInt32LittleEndian(batteryStatus.AsSpan(12), -30_000);
        var battery = BatteryTelemetryParser.ParseStatus(batteryStatus, false);
        if (battery.VoltageVolts != 15 || battery.SignedPowerWatts != -30 || battery.CalculatedCurrentAmps != -2 || battery.RemainingMilliWattHours != 90_000)
            throw new InvalidOperationException("Battery signed mW/mV conversion is incorrect.");
        BinaryPrimitives.WriteInt32LittleEndian(batteryStatus.AsSpan(12), 0);
        if (BatteryTelemetryParser.ParseStatus(batteryStatus, false).CalculatedCurrentAmps != 0)
            throw new InvalidOperationException("Valid battery zero rate was discarded.");
        var relativeBattery = BatteryTelemetryParser.ParseStatus(batteryStatus, true);
        if (relativeBattery.SignedPowerWatts is not null || relativeBattery.CalculatedCurrentAmps is not null || relativeBattery.RemainingMilliWattHours is not null || relativeBattery.VoltageVolts != 15)
            throw new InvalidOperationException("Relative battery units were interpreted as physical energy/rate.");
        BinaryPrimitives.WriteUInt32LittleEndian(batteryStatus.AsSpan(4), uint.MaxValue);
        BinaryPrimitives.WriteUInt32LittleEndian(batteryStatus.AsSpan(8), uint.MaxValue);
        BinaryPrimitives.WriteInt32LittleEndian(batteryStatus.AsSpan(12), int.MinValue);
        var unknownBattery = BatteryTelemetryParser.ParseStatus(batteryStatus, false);
        if (unknownBattery.VoltageVolts is not null || unknownBattery.SignedPowerWatts is not null || unknownBattery.CalculatedCurrentAmps is not null || unknownBattery.RemainingMilliWattHours is not null)
            throw new InvalidOperationException("Unknown battery sentinels were interpreted as measurements.");
        BinaryPrimitives.WriteUInt32LittleEndian(batteryStatus.AsSpan(8), 0);
        BinaryPrimitives.WriteInt32LittleEndian(batteryStatus.AsSpan(12), 1000);
        if (BatteryTelemetryParser.ParseStatus(batteryStatus, false).CalculatedCurrentAmps is not null)
            throw new InvalidOperationException("Battery current calculation divided by zero voltage.");
        var kelvin = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(kelvin, 2982);
        if (Math.Abs(BatteryTelemetryParser.ParseTemperature(kelvin) - 25.05) > 0.000001)
            throw new InvalidOperationException("Battery temperature tenths-Kelvin conversion is incorrect.");
        Reject(() => BatteryTelemetryParser.ParseTemperature(new byte[4]));
        Reject(() => BatteryTelemetryParser.ParseStatus(batteryStatus[..15], false));
        Reject(() => BatteryTelemetryParser.ParseTemperature(kelvin[..3]));
        foreach (var status in new[] { (Code: 0, Expected: SensorInterfaceStatus.Readable),
            (Code: 3, Expected: SensorInterfaceStatus.Unsupported),
            (Code: 4, Expected: SensorInterfaceStatus.PermissionDenied),
            (Code: 999, Expected: SensorInterfaceStatus.Failed) })
        {
            if (WindowsNvidiaTelemetryInterface.DescribeStatus("test", status.Code).Status != status.Expected)
                throw new InvalidOperationException("NVML capability/error states were conflated.");
        }
        var nativeSession = new EnergyMeterSession(10_000_000);
        nativeSession.Observe("PKG", 100_000_000_000, 10_000_000);
        var measured = nativeSession.Observe("PKG", 110_000_000_000, 110_000_000);
        if (measured is null || measured.Value.MilliWattHours != 10 || Math.Abs(measured.Value.AverageWatts - 3.6) > 0.000001)
            throw new InvalidOperationException("Native 100 ns timestamps produced incorrect energy units.");
        var gpuSession = new EnergyMeterSession(1000, 3600);
        gpuSession.Observe("GPU", 100_000, 1000);
        var gpuMeasured = gpuSession.Observe("GPU", 107_200, 3000);
        if (gpuMeasured is null || gpuMeasured.Value.MilliWattHours != 2 || Math.Abs(gpuMeasured.Value.AverageWatts - 3.6) > 0.000001)
            throw new InvalidOperationException("NVML milliJoule conversion produced incorrect session energy/power.");

        var metadata = MetadataV2("PKG", "PP0", "核显");
        var parsed = NativeEnergyMeterParser.ParseMetadata(2, metadata);
        if (parsed.Channels.Count != 3 || parsed.Channels[2].Name != "核显" || parsed.Channels[0].Unit != 0)
            throw new InvalidOperationException("Packed EMI metadata did not preserve all channel names.");
        var v1 = new byte[78];
        BinaryPrimitives.WriteUInt16LittleEndian(v1.AsSpan(70), 6);
        Encoding.Unicode.GetBytes("V1\0").CopyTo(v1, 72);
        if (NativeEnergyMeterParser.ParseMetadata(1, v1).Channels.Single().Name != "V1")
            throw new InvalidOperationException("EMI V1 metadata was not parsed.");
        var unknownUnit = MetadataV2("FUTURE");
        BinaryPrimitives.WriteUInt32LittleEndian(unknownUnit.AsSpan(68), 42);
        if (NativeEnergyMeterParser.ParseMetadata(2, unknownUnit).Channels.Single().Unit != 42)
            throw new InvalidOperationException("Unknown EMI units were silently converted to pWh.");
        Reject(() => NativeEnergyMeterParser.ParseMetadata(2, metadata[..67]));
        Reject(() => NativeEnergyMeterParser.ParseMetadata(2, metadata[..^2]));
        Reject(() => NativeEnergyMeterParser.ParseMetadata(2, MetadataV2("same", "same")));
        var invalidCount = MetadataV2("PKG");
        BinaryPrimitives.WriteUInt16LittleEndian(invalidCount.AsSpan(66), 129);
        Reject(() => NativeEnergyMeterParser.ParseMetadata(2, invalidCount));
        var oddName = MetadataV2("PKG");
        BinaryPrimitives.WriteUInt16LittleEndian(oddName.AsSpan(72), 3);
        Reject(() => NativeEnergyMeterParser.ParseMetadata(2, oddName));
        Reject(() => NativeEnergyMeterParser.ParseMetadata(2, MetadataV2("")));

        var storage = new byte[72];
        BinaryPrimitives.WriteUInt32LittleEndian(storage, 40);
        BinaryPrimitives.WriteUInt32LittleEndian(storage.AsSpan(4), 72);
        BinaryPrimitives.WriteUInt16LittleEndian(storage.AsSpan(12), 3);
        for (var index = 0; index < 3; index++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(storage.AsSpan(24 + index * 16), (ushort)index);
            BinaryPrimitives.WriteInt16LittleEndian(storage.AsSpan(26 + index * 16),
                index == 2 ? short.MinValue : (short)(index == 0 ? 55 : 0));
        }
        var temperatures = StorageTemperatureParser.Parse(storage);
        if (temperatures.Count != 2 || temperatures[0].Celsius != 55 || temperatures[1].Celsius != 0)
            throw new InvalidOperationException("Storage valid-zero/not-reported semantics were lost.");
        Reject(() => StorageTemperatureParser.Parse(storage[..^1]));
        Reject(() => StorageTemperatureParser.Parse(storage[..23]));
        var duplicateStorage = storage.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(duplicateStorage.AsSpan(40), 0);
        Reject(() => StorageTemperatureParser.Parse(duplicateStorage));
        var oversizedStorage = storage.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(oversizedStorage.AsSpan(4), 4096);
        Reject(() => StorageTemperatureParser.Parse(oversizedStorage));
        for (var length = 0; length < metadata.Length; length++)
        {
            var truncated = metadata[..length];
            Reject(() => NativeEnergyMeterParser.ParseMetadata(2, truncated));
        }
        Console.WriteLine("Passed native EMI v1/v2, all-channel/Unicode/unknown-unit, malformed-buffer, 100 ns unit, storage sentinel, and battery signed/relative/unknown/zero/temperature checks.");
    }

    private static byte[] MetadataV2(params string[] names)
    {
        var result = new byte[68 + names.Sum(name => 6 + Encoding.Unicode.GetByteCount(name + "\0"))];
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(66), (ushort)names.Length);
        var offset = 68;
        foreach (var name in names)
        {
            var bytes = Encoding.Unicode.GetBytes(name + "\0");
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(offset + 4), (ushort)bytes.Length);
            bytes.CopyTo(result, offset + 6);
            offset += 6 + bytes.Length;
        }
        return result;
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Malformed native payload was not rejected.");
    }
}
