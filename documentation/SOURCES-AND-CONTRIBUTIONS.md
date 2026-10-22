# Sources, attribution and Julian's work

## Foundation

| Source | Reuse / relationship | Attribution and scope |
| --- | --- | --- |
| [Seelen UI](https://github.com/eythaann/Seelen-UI), v2.8.8 | Actual Rust/Tauri host, Svelte toolbar/Dock/Quick Settings, React settings, resources/windows | Original authors and contributors retain credit. Git ancestry, license and notices are preserved; this fork adds adaptations. |
| [ThreeFingerDragOnWindows](https://github.com/clementgre/ThreeFingerDragOnWindows) | Foundation of the separately installed modified Touchpad Control | MIT, Clément Grennerat and contributors. This publication includes the host adapter, not its complete modified source/package. |
| Stats for Windows (C# component) | Existing publisher consumed through the v7 shared-memory ABI | Version 1.0.5 source, shared libraries and tests in `components/`. Original code AGPL-3.0-or-later by owner's explicit choice; third-party terms retained. Not a macOS Stats collector port. |
| [Brimbar](https://github.com/Cooperiano/Brimbar) | Earlier bar implementation and integration experience | Prior project, not the active host; retired WPF bar/Dock is not bundled or reactivated. |
| [AppSwitcherBar](https://github.com/adamecr/AppSwitcherBar), [Chromium taskbar utility](https://chromium.googlesource.com/chromium/src/+/refs/heads/main/chrome/installer/util/taskbar_util.cc) | ABI references for native Shell pin enumeration | References, not bundled replacement desktops; see native pin reader. |
| Apple/macOS | Interaction reference: grouping, fixed/running distinction, compact controls, precision goals | No Apple source, proprietary driver or private acceleration algorithm is claimed or supplied. No endorsement is implied. |

Root [AGPL v3](../LICENSE) text is retained. Main application metadata declares
`AGPL-3.0-or-later`; some upstream libraries declare `AGPL-3.0-only`. Those notices
remain unchanged. This is not blanket MIT relicensing. Dependencies/resources keep
their own licenses; this table does not replace them. Names/trademarks belong to
their respective owners. Upstream original README remains [here](../README.upstream.md).

## Julian Cooper / Cooperiano

Julian is the product initiator and integration maintainer for this adaptation:
defining interaction goals, choosing direct reuse over a parallel rewrite,
directing module integration, testing on actual hardware, reporting failures and
guiding revisions. Implementation, investigation and regression scaffolding were
assisted by OpenAI Codex. Upstream authorship is not reassigned to Julian or AI.

| Adaptation work | Source | Limit |
| --- | --- | --- |
| Seelen as outer host; optional Stats/Touchpad | `desktop_suite/lifecycle.rs`, settings `modules/desktopSuite/`, `components/` | Source publication, not a binary/installer release. |
| Unified navigation, English/Chinese labels, auto-save/conflict handling | settings navigation, `AutoSaveActions.tsx`, `settingsAutoSave.ts`, `preferences_store.rs` | Not all upstream languages have new suite translations. |
| Stats history and empty disabled state | `contract.rs`, `SuiteMetrics.svelte`, `metrics.ts` | ABI checks do not prove physical sensor accuracy. |
| Retained Shift 中/英 mode detection | `input.rs`, `SuiteInput.svelte` | Tested Pinyin behavior is not universal IME support. |
| Tray overflow, clock spacing, popup sizing | system-tray plugin, toolbar/theme styles, Quick Settings | Narrow and multi-monitor cases need further tests. |
| Identity, native pin migration and divider drag persistence | `widgets/weg/`, `weg/domain/`, `TaskbarBoundary.svelte` | One-time import, not two-way Windows order sync. |
| Visibility, edge reveal, desktop persistence, native-bar recovery | `weg/App.svelte`, `taskbar_guard.rs`, toolbar desktop handling | Intermittent native-bar appearance remains open. |

The touchpad direction is one shared speed/acceleration entry, not independent
finger-count multipliers. This host exposes the original component backend; it is
not a new kernel driver or a reproduction of Apple's private curve. User feedback
that dragging still needs better feel remains valid.

## Publication provenance

Prepared on 2026-10-22 from the local adaptation on public Seelen v2.8.8 ancestry.
The installed local preview was last built on 2026-10-19; this is not a newly
signed end-user release. Source was exported onto public upstream history instead
of publishing private development commits. Personal settings, original screenshots,
runtime logs, dumps, certificates, installers and machine handoffs are excluded.

The owner authorized publication and attribution, and selected AGPL for original
Stats code on 2026-10-22. Installed Stats PDB checksums matched all 31 non-generated
source documents before export. Touchpad/Activity Monitor still need their own
exact-version/license inventory; a complete reproducible assembled suite is not claimed.
