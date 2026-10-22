//! Pure lifecycle decisions; optional components are not a respawn service.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Action { Start, Stop, None }

pub fn reconcile(enabled: bool, running: bool) -> Action {
    match (enabled, running) {
        (true, false) => Action::Start,
        (false, true) => Action::Stop,
        _ => Action::None,
    }
}

pub fn known_component(id: &str) -> bool { matches!(id, "stats" | "touchpad") }

#[cfg(test)]
mod tests {
    use super::*;
    #[test] fn start_missing_enabled() { assert_eq!(reconcile(true, false), Action::Start); }
    #[test] fn keep_disabled_empty() { assert_eq!(reconcile(false, false), Action::None); }
    #[test] fn stop_disabled_running() { assert_eq!(reconcile(false, true), Action::Stop); }
    #[test] fn repeated_start_is_idempotent() { assert_eq!(reconcile(true, true), Action::None); }
    #[test] fn accepts_only_component_ids() {
        for id in ["stats", "touchpad"] { assert!(known_component(id)); }
        for id in ["activity_monitor", "../stats", "Stats", "", "cmd.exe"] { assert!(!known_component(id)); }
    }
}
