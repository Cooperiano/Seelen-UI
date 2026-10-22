# Optional Stats component source

`Stats/` contains Stats for Windows 1.0.5; `Shared/HardwareTelemetry` owns Windows
collection and `Shared/Monitoring.Contracts` owns the v7 publication protocol.
`Stats.Tests/` is the existing executable regression harness, not an xUnit project.
The host source reads these contracts; it does not duplicate collection.

This export was traced from the installed Stats portable PDB and all 31 non-obj
Stats source documents matched their embedded checksums (26 SHA-256, 5 XAML SHA-1).
That proves the source-document correspondence, not a byte-identical rebuild or
that every newly restored third-party dependency equals the installed dependency.
Private machine paths/PDBs and sensor reports are not published.

## Build / bounded checks (Windows, .NET 10 SDK)

```powershell
dotnet build components/Stats/Stats.csproj -c Debug
dotnet run --project components/Stats.Tests/Stats.Tests.csproj -c Debug -- --suite-contract-only
```

The second command checks route/availability/launch plans without starting Stats,
sampling hardware, controlling input or launching the host. Running the harness
without that flag includes WPF fixtures and hardware-dependent checks; do not treat
those as portable CI tests or run a second production publisher alongside your suite.
The original full harness is retained for authorized local diagnostics.

The application can be developed independently, but an existing suite installation
uses an explicit component manifest and central-settings pointer. Building is not
installation, autostart configuration or a reproducible complete-suite installer.
Do not overwrite a running installed collector or copy user preferences into Git.

## License and scope

The owner chose AGPL for the original component code. [NOTICE](NOTICE.md),
[LICENSE](LICENSE) and [dependency attribution](THIRD-PARTY.md) apply. Settings,
hardware reports, source-machine docs/scripts, publish folders and binaries are
excluded. Modified Touchpad Control and Activity Monitor sources remain outside
this publication; their integration points remain in the Seelen host.
