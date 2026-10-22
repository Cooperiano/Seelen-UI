pub fn normalize_program_path(path: &str) -> String {
    path.to_lowercase().replace('/', "\\")
}

/// Mirrors applicationIdentity.ts. An executable path must not merge distinct app IDs.
pub fn matches_application(
    item_id: Option<&str>,
    item_path: &str,
    item_command: Option<&str>,
    window_id: Option<&str>,
    window_path: &str,
    inventory: &[(Option<&str>, String)],
) -> bool {
    let item_id = item_id.filter(|id| !id.is_empty());
    let window_id = window_id.filter(|id| !id.is_empty());
    if let (Some(item_id), Some(window_id)) = (item_id, window_id) {
        return item_id == window_id;
    }
    let path = normalize_program_path(window_path);
    if path.is_empty()
        || (normalize_program_path(item_path) != path
            && item_command.map(normalize_program_path).as_deref() != Some(path.as_str()))
    {
        return false;
    }
    let identities: std::collections::HashSet<_> = inventory
        .iter()
        .filter(|(_, candidate_path)| normalize_program_path(candidate_path) == path)
        .filter_map(|(id, _)| id.filter(|id| !id.is_empty()))
        .collect();
    match item_id {
        Some(id) => identities.iter().all(|candidate| *candidate == id),
        None => identities.len() <= 1,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn explicit_ids_are_authoritative() {
        assert!(matches_application(
            Some("app"),
            "",
            None,
            Some("app"),
            "",
            &[]
        ));
        assert!(!matches_application(
            Some("a"),
            "app.exe",
            None,
            Some("b"),
            "app.exe",
            &[]
        ));
    }

    #[test]
    fn legacy_pin_adopts_one_identity() {
        let inventory = [(Some("app"), "C:/App.exe".into())];
        assert!(matches_application(
            None,
            "c:\\app.exe",
            None,
            Some("app"),
            "C:/App.exe",
            &inventory
        ));
    }

    #[test]
    fn legacy_pin_does_not_merge_browser_profiles() {
        let inventory = [(Some("a"), "app.exe".into()), (Some("b"), "app.exe".into())];
        assert!(!matches_application(
            None,
            "app.exe",
            None,
            Some("a"),
            "app.exe",
            &inventory
        ));
        assert!(!matches_application(
            Some("a"),
            "app.exe",
            None,
            None,
            "app.exe",
            &inventory
        ));
    }

    #[test]
    fn no_identity_does_not_match_empty_launch_target() {
        assert!(!matches_application(None, "", None, None, "", &[]));
    }

    #[test]
    fn relaunch_path_matches_legacy_shortcut() {
        assert!(matches_application(
            None,
            "app.lnk",
            Some("APP.exe"),
            None,
            "app.exe",
            &[]
        ));
    }

    #[test]
    fn explicit_item_can_match_unidentified_window_without_competing_identity() {
        let inventory = [(Some("a"), "app.exe".into()), (None, "app.exe".into())];
        assert!(matches_application(
            Some("a"),
            "app.exe",
            None,
            None,
            "app.exe",
            &inventory
        ));
    }
}
