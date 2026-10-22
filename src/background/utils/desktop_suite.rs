use std::path::{Path, PathBuf};

pub const MODULE_IDS: [&str; 3] = ["stats", "touchpad", "activity_monitor"];

/// Reuse installed modules. No new collector, startup task, executable scan, or arbitrary launch string.
pub fn module_path(module: &str, desktop: &Path, local_data: &Path) -> Option<PathBuf> {
    match module {
        "stats" => Some(desktop.join("Stats.lnk")),
        "touchpad" => Some(local_data.join("Microsoft/WindowsApps/ThreeFingerDragOnWindows.exe")),
        "activity_monitor" => {
            Some(local_data.join("Programs/Activity Monitor/activity-monitor.exe"))
        }
        _ => None,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn uses_known_folders_not_a_developer_username() {
        assert_eq!(
            module_path("stats", Path::new("D:/Desktop"), Path::new("E:/Local")),
            Some(PathBuf::from("D:/Desktop/Stats.lnk"))
        );
        assert_eq!(
            module_path("touchpad", Path::new("D:/Desktop"), Path::new("E:/Local")),
            Some(PathBuf::from(
                "E:/Local/Microsoft/WindowsApps/ThreeFingerDragOnWindows.exe"
            ))
        );
    }

    #[test]
    fn rejects_arbitrary_programs_and_path_traversal() {
        for input in ["cmd", "../stats", "C:/unknown.exe", "Stats", ""] {
            assert!(module_path(input, Path::new("D:/Desktop"), Path::new("E:/Local")).is_none());
        }
    }

    #[test]
    fn every_advertised_module_has_a_target() {
        for id in MODULE_IDS {
            assert!(module_path(id, Path::new("D:/Desktop"), Path::new("E:/Local")).is_some());
        }
    }
}
