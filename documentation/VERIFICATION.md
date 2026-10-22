# Verification and known issues

Experimental source snapshot, not a stable binary release. Keep source-policy
assertions, executable behavior tests, process identity, visible windows, physical
feel and long-duration use distinct. Some tests inspect source structure; passing
them does not prove UI behavior. Heartbeats/ACKs are not screenshots or user acceptance.

The local 2026-10-19 delivery reported 68 Deno tests, 88 Node tests, frontend type
checks and targeted Rust taskbar checks passing, followed by a local build/install.
Those are historical checks, not proof that public CI has run or a fresh checkout
produces an identical signed package. Fresh publication checks/CI status are recorded
in the pull request. Active CI publishes no binary and changes no local installation.

## Bounded user confirmations

- Shift Chinese/English label followed changes with the tested input method.
- Quick Settings reopened without the previous narrow/cropped state.
- Win+D retained the topbar in the subsequent user test.
- Latest Dock edge reveal/hide worked in one ordinary-window user test.

These do not certify every application, display, IME or Windows version.

## Open issues

1. Original Windows taskbar occasionally appears. A current background read found
   host, Stats, recovery guard and fresh Dock heartbeat active, native bar hidden.
   It did not reproduce the episode. Root cause and a long-duration fix are **not
   confirmed**. Keep native-bar recovery; do not disable safety to mask failure.
2. Dock stayed expanded after long use. `UntilActivated` attention policy could
   keep it open for a highlighted app; the local setting was changed to `Never`.
   Long-duration acceptance remains pending; this is not every possible cause.
3. Three-finger dragging works in the user's test, but curve/feel need improvement.
   Highest-speed precision requires actual input measurements and hands-on testing.
4. New suite labels cover English and Simplified Chinese; other locales may use
   fallbacks. Full multilingual completion is not claimed.
5. No fresh signed public installer, clean-machine full-suite setup, all-monitor
   recovery or real sign-in matrix is certified by this publication.

For reports, share Windows version, display scale/count, Dock mode, fullscreen/
desktop context and minimal visible behavior. Do not post raw settings, window
inventories, dumps, personal screenshots or credentials in public issues. Typed
text is not needed for IME-mode diagnosis.
