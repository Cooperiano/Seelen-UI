pub const SUITE_BUILD: bool = cfg!(feature = "desktop-suite");
pub const APP_ID: &str = if SUITE_BUILD {
    "com.cooperiano.stats-desktop-dev"
} else {
    "com.seelen.seelen-ui"
};
pub const APP_PIPE: &str = if SUITE_BUILD {
    "stats-desktop-dev"
} else {
    "seelen-ui"
};
pub const SERVICE_PIPE: &str = if SUITE_BUILD {
    "stats-desktop-dev-service"
} else {
    "seelen-ui-service"
};
pub const APP_MUTEX: &str = if SUITE_BUILD {
    "Stats-Desktop-Dev-Instance"
} else {
    "Seelen-UI-Instance"
};
pub const SERVICE_MUTEX: &str = if SUITE_BUILD {
    "Stats-Desktop-Dev-Service-Instance"
} else {
    "Seelen-UI-Service-Instance"
};
pub const TASK_FOLDER: &str = if SUITE_BUILD {
    "\\StatsDesktopDev"
} else {
    "\\Seelen"
};
pub const SERVICE_TASK: &str = if SUITE_BUILD {
    "Stats Desktop Dev Service"
} else {
    "Seelen UI Service"
};
pub const LEGACY_TASK: &str = if SUITE_BUILD {
    "Stats-Desktop-Dev"
} else {
    "Seelen-UI"
};

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn suite_identity_is_not_an_upstream_runtime_identity() {
        if SUITE_BUILD {
            assert_ne!(APP_ID, "com.seelen.seelen-ui");
            assert_ne!(APP_PIPE, "seelen-ui");
            assert_ne!(SERVICE_PIPE, "seelen-ui-service");
            assert_ne!(TASK_FOLDER, "\\Seelen");
            assert_ne!(SERVICE_TASK, "Seelen UI Service");
            assert_ne!(APP_MUTEX, "Seelen-UI-Instance");
            assert_ne!(SERVICE_MUTEX, "Seelen-UI-Service-Instance");
        }
    }
}
