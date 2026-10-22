# Desktop Suite architecture and development

Base: upstream Seelen UI v2.8.8, commit `53274fa9f45ad1b0ebd798e2d464858a213d9022`.
Public ancestry is preserved. Private development commits, settings, logs and
machine-specific handoffs are not imported.

## Boundaries

| Responsibility | Implementation |
| --- | --- |
| Desktop host and windows | Seelen Rust/Tauri backend, Svelte widgets, React settings |
| Suite lifecycle and component preferences | `src/background/modules/desktop_suite/` |
| Unified settings, autosave and conflicts | settings `modules/desktopSuite/`, `state/settingsAutoSave.ts` |
| Telemetry display, not collection | `SuiteMetrics.svelte`, `desktop_suite/contract.rs` |
| Shift Chinese/English mode | `SuiteInput.svelte`, `desktop_suite/input.rs` |
| Identity, ordering and pin migration | `widgets/weg/`, `weg/domain/` |
| Native taskbar hiding and recovery | `taskbar_guard.rs`, `taskbar_policy.rs` |
| Compact toolbar and popup theme | `preview-resources/suite-fit/` |

Stats is the external metrics publisher; the host reads the 272-byte
`Local\Monitoring.Metrics.v7` ABI. Disabled/unavailable Stats leaves an empty metrics
area. Touchpad owns input and its Windows tracking backend; no second pointer
multiplier is added here. Component preferences live in the main window, while
Quick Settings and metric-detail popovers remain separate transient surfaces.

Stats source and shared libraries are in `components/`; see its separate .NET
build and bounded contract checks. Touchpad/helper sources live in the separate
[MIT adaptation fork](https://github.com/Cooperiano/ThreeFingerDragOnWindows).
Activity Monitor is not bundled.
Runtime component discovery/configuration is implemented in
`desktop_suite/lifecycle.rs`. Central tracking needs compatible Touchpad 48 or newer;
metrics need a compatible Stats publisher built separately. A checkout is not a complete
end-user suite installer. Do not fill missing components from unverified mirrors.

Settings auto-save using revision-aware, bounded, atomic writes. Conflicts and
failures retain the edit and surface errors. Configuring a disabled component
does not enable it. Touchpad preference commits can briefly restart an already
running input component: this is not fully interruption-free hot reload.
Acceleration changes require explicit confirmation. Unknown fields and backups survive.

Native pin migration reads the Shell sequence, backs up state and writes a durable
receipt. It never changes Explorer's pins. A receipt blocks automatic replay;
subsequent suite order is authoritative, not continuously mirrored to Windows.
Do not delete receipts to overwrite later user edits. Fixed apps precede running
apps with a divider. Cross-divider drops commit membership and order together.

Dock display mode controls taskbar replacement intent. A user-level guard in the
same executable hides the original taskbar only while required Dock surfaces have
fresh ready heartbeats. It restores appbar flags and the native taskbar on failure
or exit. Do not disable recovery to mask an intermittent unwanted taskbar.
This is not a registry shell replacement or an elevated service.

## Windows build

Requirements follow upstream: MSVC C++ build tools/Windows SDK, WebView2, Node.js
(CI uses 24), Deno 2 and the pinned `rust-toolchain.toml`. Network access is needed
for dependency/bindings preparation. From a fresh checkout:

```powershell
npm ci
npm run suite:test
npm run type-check
npm run suite:check
npm run build:ui
npx tauri build --debug --no-bundle --config src/tauri.suite.conf.json --config src/tauri.suite.incremental.conf.json
cargo build -p sluhk --features desktop-suite
```

`npm ci` runs upstream library-generation preinstall (`npm run build:lib`). Never
hand-edit generated command bindings. The incremental config skips UI rebuilding,
so run `build:ui` first. The hook DLL must use the same suite feature/IPC identity.
Use debug builds for iteration, not `cargo build --release` for testing.

The suite override enables `desktop-suite` and isolated identity
`com.cooperiano.stats-desktop-dev`; profile, IPC and mutexes are separate from
upstream. Do not use bare upstream `npm run dev`, overwrite an official profile,
or run competing desktop bars together. This is a development identity, not a
claim of signed public packaging readiness.

The suite feature disables the official updater, account/cloud access, analytics,
Discord activity, clipboard capture and upstream automatic login-task creation.
Its optional current-user autostart belongs to the suite lifecycle manager.

## Recovery and delivery

`slu suite-status` reads status; `slu suite-exit` requests normal whole-suite exit.
Confirm Explorer's taskbar returns before replacing binaries. Preserve settings
and rollback packages; do not terminate Explorer. Actual rendering, clicks,
physical input, real sign-in and abrupt-exit/multi-monitor recovery are separate
gates from unit tests. See [verification](VERIFICATION.md).

Inherited publishing/announcement/auto-merge workflows are archived outside the
active workflow directory. This source publication does not sign or publish an
installer, update release tags or change the local installation.
