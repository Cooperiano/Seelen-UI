using HardwareTelemetry.Models;
using System.Runtime.InteropServices;

namespace HardwareTelemetry.Services;

// Compatibility diagnosis only: legacy COM Sensor API is deprecated, not a new production collector.
public static class WindowsLegacySensorDiscovery
{
    private static readonly Guid ManagerClass = new("77A1C827-FCD2-4689-8915-9D613CC5FA3E");
    private static readonly (string Name, Guid Id)[] Categories = [
        ("Environmental", new("323439AA-7F66-492B-BA0C-73E9AA0A65D5")),
        ("Electrical", new("FB73FCD8-FC4A-483C-AC58-27B691C6BEFF"))
    ];

    public static IReadOnlyList<LegacySensorCategoryDiscovery> Capture()
    {
        object? owner = null;
        try
        {
            owner = Activator.CreateInstance(Type.GetTypeFromCLSID(ManagerClass, throwOnError: true)!);
            if (owner is not ILegacyManager manager)
                return Categories.Select(category => Result(category, unchecked((int)0x80040154), null, "QuerySensorManagerInterface")).ToList();
            return Categories.Select(category => ReadCategory(manager, category)).ToList();
        }
        catch (COMException exception)
        {
            return Categories.Select(category => Result(category, exception.HResult, null, "ActivateSensorManager")).ToList();
        }
        finally { Release(owner); }
    }

    private static LegacySensorCategoryDiscovery ReadCategory(ILegacyManager manager, (string Name, Guid Id) category)
    {
        ILegacyCollection? collection = null;
        var operation = "GetSensorsByCategory";
        try
        {
            var id = category.Id;
            var result = manager.GetSensorsByCategory(ref id, out collection);
            if (result < 0 || collection is null)
                return Result(category, result, null, operation);
            operation = "GetCount";
            result = collection.GetCount(out var count);
            return Result(category, result, result >= 0 ? count : null, operation);
        }
        catch (COMException exception) { return Result(category, exception.HResult, null, operation); }
        finally { Release(collection); }
    }

    private static LegacySensorCategoryDiscovery Result((string Name, Guid Id) category, int result, uint? count, string operation) =>
        new(category.Name, category.Id, operation, count is <= 512 ? count : null,
            Describe(category.Name + "/" + operation, result, count));

    internal static SensorInterfaceProbe Describe(string endpoint, int result, uint? count)
    {
        var status = result switch
        {
            unchecked((int)0x80070005) or unchecked((int)0x800704ec) => SensorInterfaceStatus.PermissionDenied,
            unchecked((int)0x80070490) or unchecked((int)0x80040154) => SensorInterfaceStatus.NoInterface,
            unchecked((int)0x80004001) => SensorInterfaceStatus.Unsupported,
            < 0 => SensorInterfaceStatus.Failed,
            _ => count switch { 0 => SensorInterfaceStatus.NoInterface, > 512 => SensorInterfaceStatus.InvalidPayload,
                null => result == 1 ? SensorInterfaceStatus.NoInterface : SensorInterfaceStatus.InvalidPayload,
                _ => SensorInterfaceStatus.Readable }
        };
        return new("Windows Sensor API (legacy diagnostic)", endpoint, status,
            status switch {
                SensorInterfaceStatus.Readable => $"{count} registered sensors; enumeration is not a measured sensor value. Named data fields still require independent verification.",
                SensorInterfaceStatus.NoInterface => result == unchecked((int)0x80040154)
                    ? "Legacy SensorManager COM class/interface unavailable; no physical-sensor absence claim."
                    : "No currently published legacy sensors in this category; this does not prove physical sensor absence.",
                SensorInterfaceStatus.PermissionDenied => "Access or policy denied. No permission request, prompt, elevation or policy change attempted.",
                SensorInterfaceStatus.InvalidPayload => "Collection count missing or exceeds the diagnostic bound.",
                _ => $"HRESULT 0x{result:X8}; no synthetic readings."
            }, result < 0 ? result : null);
    }

    private static void Release(object? value)
    {
        // These RCWs belong solely to this synchronous discovery, not a shared production collector.
        if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
    }

    // Read-only ABI prefixes from Windows SDK 10.0.26100.0 SensorsApi.h.
    // Permission, event subscription, property-set and location/motion methods are intentionally absent.
    [ComImport, Guid("BD77DB67-45A8-42DC-8D00-6DCF15F8377A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ILegacyManager
    {
        [PreserveSig] int GetSensorsByCategory(ref Guid category, out ILegacyCollection? collection);
    }

    [ComImport, Guid("23571E11-E545-4DD8-A337-B89BF44B10DF"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ILegacyCollection
    {
        [PreserveSig] int GetAt(uint index, out IntPtr sensor); // ABI slot only; never called by discovery.
        [PreserveSig] int GetCount(out uint count);
    }
}

public sealed record LegacySensorCategoryDiscovery(string Category, Guid CategoryId, string Operation, uint? RegisteredCount,
    SensorInterfaceProbe Probe);
