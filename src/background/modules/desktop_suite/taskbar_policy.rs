//! Failure defaults to the original shell, never to an inaccessible desktop.
/// Dock configuration is the sole replacement intent. Accept legacy overlap mode
/// during migration; settings normalize it to the two-choice auto-hide policy.
pub fn configured(enabled:bool, hide_mode:&str)->bool {
    enabled && matches!(hide_mode, "Never" | "Always" | "OnOverlap")
}
pub fn may_hide(enabled:bool,parent_alive:bool,dock_ready:bool,heartbeat_age_ms:u64)->bool {
    enabled && parent_alive && dock_ready && heartbeat_age_ms<5000
}
pub fn should_restore_bar(original_state:u32, _original_visibility:Option<bool>)->bool {
    // SW_HIDE disables the shell window; it is not Explorer's automatic edge
    // collapse. A visibility sample can be transiently false when the lease is
    // renewed. Restore ShowWindow for every valid original flag combination;
    // the caller separately restores the exact ABM state, including auto-hide.
    original_state <= 3
}
