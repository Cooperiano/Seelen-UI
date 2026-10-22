# Suite topbar integration

The host reuses Seelen's Fancy Toolbar, widget windows, positioning, themes and
Quick Settings, rather than introducing a second UI framework.

- Stats remains the single telemetry publisher. `contract.rs` validates its v7
  272-byte ABI/seqlock; the Rust bridge reads it and `SuiteMetrics.svelte` displays
  configured order, units and colors with bounded received-sample history.
- `SuiteInput.svelte` polls a read-only Pinyin adapter sequentially at 100 ms.
  Focus, root window, owner PID/TID, HKL and default IME are checked around bounded
  samples. Ambiguous profiles/timeouts are unknown, not guessed English. No text
  capture or keyboard hook is used to detect mode.
- System tray uses the original plugin and live add/update/remove events. Suite
  profiles show registered visible application icons inline; native system-control
  duplicates remain excluded. Icon presence is not a claim about login state.
- Only the app tray scrolls horizontally within its width budget. Time, input
  mode and Quick Settings retain independent space. Clock seconds are supported
  through the saved Moment format containing `ss`; they are not forced on users.
- Quick Settings retains Seelen actions. Corrected popup sizing avoids a self-`vw`
  clamp feedback loop. The optional `preview-resources/suite-fit` resource supplies
  compact styling; loading it is not a new signed host release.

Theme installation uses upstream resource commands in the isolated profile.
Keep `@default/theme`; activate `@cooperiano/suite-fit` only if desired. Avoid
reloading an already active resource repeatedly (the upstream version can append
duplicate activations); its watcher reloads changed files. Do not overwrite user
theme customizations. Build the tray hook with the same suite IPC identity.

Source assertions, ABI reads, actual pixels, clicking and user Shift/gesture
tests are separate evidence. A wrong-window screenshot or an IPC acknowledgement
must never be reported as visual/physical acceptance. See [verification](VERIFICATION.md).
