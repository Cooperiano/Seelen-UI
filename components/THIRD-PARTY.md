# Component dependencies and interface references

## Managed packages

- `LibreHardwareMonitorLib` 0.9.6: LibreHardwareMonitor contributors,
  [source](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/tree/v0.9.6),
  [MPL 2.0 license](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/v0.9.6/LICENSE).
  Acquired through NuGet; its code/binaries/drivers are not relicensed or vendored here.
- `System.Management` 10.0.12: .NET contributors,
  [dotnet/runtime](https://github.com/dotnet/runtime),
  [MIT license](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT).
- Transitive dependencies remain governed by their own NuGet license metadata.
  Inspect `dotnet list ... package --include-transitive` and include notices when
  packaging. This source publication is not a binary dependency redistribution.

## References, not bundled applications

- [exelban/Stats](https://github.com/exelban/stats): macOS monitoring/module UX
  inspiration. This Windows C# collector is not a Swift/SMC implementation port.
- [GHelper](https://github.com/seerge/g-helper): references for named ASUS ACPI
  DSTS and CPU interfaces, not permission to scan/write hardware registers or
  redistribute/relicense that application.
- [Intel PresentMon](https://github.com/GameTechDev/PresentMon): optional external
  FPS helper/interface. No helper binary/service is included or auto-installed.
- Microsoft Windows SDK/Win32/WMI/DXGI documentation supplies platform interface
  definitions. NVIDIA NVML and Intel driver interfaces depend on their installed
  vendor software; no vendor driver is supplied.

Interface availability, source support and actual readable values are different.
Missing sensor readings remain unavailable; limits/estimates must not be shown as
measured instantaneous power. No automatic elevation or hardware-driver installation
is authorized by building this source.
