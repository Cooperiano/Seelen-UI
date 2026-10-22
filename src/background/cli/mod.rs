mod debugger;
mod self_pipe;
pub mod shortcuts;
mod svc_pipe;
mod uri;

pub use self_pipe::SelfPipe;
pub use svc_pipe::ServicePipe;

use std::sync::atomic::Ordering;

use slu_ipc::{AppIpc, commands::AppCommand, messages::AppMessage};

use crate::{
    error::Result,
    resources::cli as resources_cli,
    virtual_desktops::cli as vd_cli,
    widgets::{
        cli as widget_cli, popups::cli as popups_cli, show_settings,
        task_switcher::cli as task_switcher_cli, wallpaper_manager::cli as wallpaper_cli,
        weg::cli as weg_cli, window_manager::cli as wm_cli,
    },
};

/// WARNING: seelen-ui.exe CLI commands are deprecated.
///
/// Use `slu` instead.
#[derive(Debug, clap::Parser)]
#[command(version, name = "Seelen UI")]
struct MainCli {
    /// Open the primary suite settings, starting the host when necessary.
    #[arg(long, default_value_t)]
    settings: bool,
    #[arg(long, value_parser = ["stats", "touchpad"])]
    settings_route: Option<String>,
    #[arg(long, default_value_t)]
    silent: bool,
    #[arg(long, default_value_t)]
    verbose: bool,
    /// Path or URI to open (e.g. from the Windows protocol handler).
    uri: Option<String>,
}

/// Called at startup by the main executable. If a URI is present it is forwarded
/// to the running instance via IPC and the process exits; otherwise returns Ok(())
/// so normal app startup continues.
pub async fn handle_console_client() -> Result<()> {
    use clap::Parser;
    let cli = match MainCli::try_parse() {
        Ok(cli) => cli,
        Err(e) => e.exit(),
    };

    if cli.silent {
        crate::SILENT.store(true, Ordering::SeqCst);
    }
    if cli.verbose {
        crate::VERBOSE.store(true, Ordering::SeqCst);
        println!("Received args: {:#?}", std::env::args().collect::<Vec<_>>());
        println!("Parsed CLI: {cli:#?}");
    }

    #[cfg(feature = "desktop-suite")]
    if let Some(module) = cli.settings_route {
        if AppIpc::send(AppMessage::Command(AppCommand::SuiteSettings { module: module.clone() })).await.is_ok() {
            std::process::exit(0);
        }
        // The regular startup path creates the host; route is delivered after it is ready.
        crate::OPEN_SUITE_SETTINGS.store(true, Ordering::SeqCst);
        let runtime = tokio::runtime::Handle::current();
        std::thread::spawn(move || {
            for _ in 0..100 {
                std::thread::sleep(std::time::Duration::from_millis(150));
                if runtime.block_on(AppIpc::send(AppMessage::Command(AppCommand::SuiteSettings { module: module.clone() }))).is_ok() { break; }
            }
        });
    }
    if cli.settings && slu_ipc::identity::SUITE_BUILD {
        if AppIpc::send(AppMessage::Command(AppCommand::Settings)).await.is_ok() {
            std::process::exit(0);
        }
        crate::OPEN_SUITE_SETTINGS.store(true, Ordering::SeqCst);
    }

    if let Some(uri) = cli.uri {
        AppIpc::send(AppMessage::OpenUri(uri))
            .await
            .map_err(|_| "Can't establish connection, ensure Seelen UI is running.")?;
        std::process::exit(0);
    }

    Ok(())
}

pub async fn process_app_command(cmd: AppCommand) -> Result<()> {
    match cmd {
        #[cfg(feature = "desktop-suite")]
        AppCommand::SuiteTaskbarPins => {
            let pins = tokio::task::spawn_blocking(crate::widgets::weg::native_taskbar::get_windows_taskbar_pinned_apps).await??;
            crate::utils::atomic_write_file(&crate::utils::constants::SEELEN_COMMON.app_data_dir().join("native-taskbar-pins-read.json"), &serde_json::to_vec_pretty(&pins)?)?;
        }
        #[cfg(feature = "desktop-suite")]
        AppCommand::SuiteTaskbarMigrate => {
            let count = tokio::task::spawn_blocking(|| crate::state::application::WEG_ITEMS_MANAGER.migrate_native_order_once()).await??;
            if count > 0 {
                crate::app::emit_to_webviews(seelen_core::handlers::SeelenEvent::WegItemsChanged, crate::state::application::WEG_ITEMS_MANAGER.get());
            }
            crate::utils::atomic_write_file(&crate::utils::constants::SEELEN_COMMON.app_data_dir().join("native-taskbar-migration-read.json"), &serde_json::to_vec(&serde_json::json!({"imported":count}))?)?;
        }
        #[cfg(feature = "desktop-suite")]
        AppCommand::SuiteTaskbarSessions => {
            let sessions=crate::modules::desktop_suite::app_sessions::snapshot();
            crate::utils::atomic_write_file(&crate::utils::constants::SEELEN_COMMON.app_data_dir().join("taskbar-sessions-read.json"),&serde_json::to_vec(&sessions)?)?;
        }
        #[cfg(feature = "desktop-suite")]
        AppCommand::SuiteTaskbar { enabled } => {
            crate::modules::desktop_suite::taskbar_guard::set_enabled(enabled)?;
        }
        #[cfg(feature = "desktop-suite")]
        AppCommand::SuiteSettings { module } => {
            if !["stats", "touchpad"].contains(&module.as_str()) { return Err("Unknown suite settings page".into()); }
            crate::widgets::show_settings_at(&format!("/{module}"))?;
        }
        #[cfg(feature = "desktop-suite")]
        AppCommand::SuitePreferences { module, patch, revision } => {
            let snapshot = tokio::task::spawn_blocking(move || {
                let manager = crate::modules::desktop_suite::lifecycle::SuiteManager::instance();
                if let (Some(patch), Some(revision)) = (patch, revision) {
                    if patch.len() > 131072 { return Err("Settings patch too large".into()); }
                    manager.save_component_preferences(&module, &revision, &serde_json::from_str(&patch)?)
                } else { manager.read_component_preferences(&module) }
            }).await??;
            crate::utils::atomic_write_file(&crate::utils::constants::SEELEN_COMMON.app_data_dir().join("suite-preferences-read.json"), &serde_json::to_vec_pretty(&snapshot)?)?;
        }
        #[cfg(feature = "desktop-suite")]
        AppCommand::SuiteStatus => {
            let snapshot = tokio::task::spawn_blocking(|| crate::modules::desktop_suite::lifecycle::SuiteManager::instance().state()).await??;
            crate::utils::atomic_write_file(&crate::utils::constants::SEELEN_COMMON.app_data_dir().join("suite-state-read.json"), &serde_json::to_vec_pretty(&snapshot)?)?;
        }
        #[cfg(feature = "desktop-suite")]
        AppCommand::SuiteComponent { module, enabled } => {
            tokio::task::spawn_blocking(move || crate::modules::desktop_suite::lifecycle::SuiteManager::instance().set_enabled(&module, enabled)).await??;
        }
        #[cfg(feature = "desktop-suite")]
        AppCommand::SuiteExit => {
            // Let IPC acknowledge before the event loop closes all owned windows/AppBars.
            crate::get_tokio_handle().spawn(async {
                tokio::time::sleep(std::time::Duration::from_millis(100)).await;
                crate::app::get_app_handle().exit(0);
            });
        }
        AppCommand::Settings => {
            show_settings()?;
        }
        AppCommand::VirtualDesk(command) => {
            vd_cli::process(command)?;
        }
        AppCommand::Debugger(command) => {
            debugger::process(command)?;
        }
        AppCommand::WindowManager(command) => {
            wm_cli::process(command)?;
        }
        AppCommand::Weg(command) => {
            weg_cli::process(command)?;
        }
        AppCommand::Widget(command) => {
            widget_cli::run(command)?;
        }
        AppCommand::Resource(command) => {
            resources_cli::process(command).await?;
        }
        AppCommand::Popup(command) => {
            popups_cli::process(command)?;
        }
        AppCommand::TaskSwitcher(command) => {
            task_switcher_cli::process(command)?;
        }
        AppCommand::Wallpaper(command) => {
            wallpaper_cli::process(command)?;
        }
        AppCommand::ToggleShortcutsPause => {
            shortcuts::toggle_pause()?;
        }
        _ => {
            return Err("Command does not support instance execution".into());
        }
    }
    Ok(())
}
