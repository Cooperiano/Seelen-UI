use std::{collections::HashMap, path::PathBuf};

use seelen_core::system_state::{RelaunchArguments, StartMenuLayout, StartMenuLayoutItem};

use slu_ipc::{ServiceIpc, messages::SvcAction};
use tauri::{Manager, WebviewWindow};

use crate::{
    app::{SeelenUI, get_app_handle},
    error::Result,
    utils::{
        self,
        constants::SEELEN_COMMON,
        icon_extractor::{request_icon_extraction_from_file, request_icon_extraction_from_umid},
        pwsh::PwshScript,
    },
    widgets::{
        permissions::{WidgetPerm, request_widget_permission},
        popups::shortcut_registering::REG_SHORTCUT_DATA,
    },
    windows_api::{WindowsApi, string_utils::WindowsString},
};

impl crate::tauri_handlers::Handlers {
    pub fn get_desktop_suite_modules() -> Result<Vec<String>> {
        let resolver = get_app_handle().path();
        let desktop = resolver.desktop_dir()?;
        let local_data = resolver.local_data_dir()?;
        Ok(utils::desktop_suite::MODULE_IDS
            .iter()
            .filter(|id| {
                  if slu_ipc::identity::SUITE_BUILD && matches!(**id, "stats" | "touchpad") {
                      return crate::modules::desktop_suite::lifecycle::component_path(id).is_ok_and(|p| p.is_file());
                  }
                utils::desktop_suite::module_path(id, &desktop, &local_data)
                    .is_some_and(|path| path.is_file())
            })
            .map(|id| (*id).to_owned())
            .collect())
    }

    pub fn open_desktop_suite_module(webview: WebviewWindow, module: String) -> Result<()> {
        request_widget_permission(&webview, WidgetPerm::Run)?;
        if slu_ipc::identity::SUITE_BUILD && matches!(module.as_str(), "stats" | "touchpad") {
            return crate::widgets::show_settings_at(&format!("/{module}"));
        }
        let resolver = get_app_handle().path();
        let path = utils::desktop_suite::module_path(
            &module,
            &resolver.desktop_dir()?,
            &resolver.local_data_dir()?,
        )
        .filter(|path| path.is_file())
        .ok_or("Desktop suite module is not installed")?;
        WindowsApi::execute(path.to_string_lossy().to_string(), None, None, false)
    }

    pub fn log_from_webview(level: u8, message: String, location: String) {
        let level = match level {
            1 => log::Level::Trace,
            2 => log::Level::Debug,
            3 => log::Level::Info,
            4 => log::Level::Warn,
            _ => log::Level::Error,
        };
        log::log!(target: &location, level, "{message}");
    }

    pub fn open_file(webview: tauri::WebviewWindow, path: String) -> Result<()> {
        request_widget_permission(&webview, WidgetPerm::OpenFile)?;
        WindowsApi::execute(path, None, None, false)
    }

    pub fn select_file_on_explorer(path: PathBuf) -> Result<()> {
        let path = path.to_string_lossy().to_string();
        std::process::Command::new(SEELEN_COMMON.system_dir().join("explorer.exe"))
            .args(["/select,", &path])
            .spawn()?;
        Ok(())
    }

    pub fn run(
        webview: tauri::WebviewWindow,
        program: String,
        args: Option<RelaunchArguments>,
        working_dir: Option<PathBuf>,
        elevated: bool,
    ) -> Result<()> {
        request_widget_permission(&webview, WidgetPerm::Run)?;
        let args = args.map(|args| args.to_string());
        WindowsApi::execute(program, args, working_dir, elevated)
    }

    pub fn app_exit() {
        get_app_handle().exit(0);
    }

    pub fn app_restart() {
        get_app_handle().restart();
    }

    pub fn is_dev_mode() -> bool {
        tauri::is_dev()
    }

    pub fn has_fixed_runtime() -> bool {
        crate::utils::has_fixed_runtime()
    }

    pub fn is_appx_package() -> bool {
        crate::utils::is_running_as_appx()
    }

    pub fn get_user_envs() -> HashMap<String, String> {
        std::env::vars().collect::<HashMap<String, String>>()
    }

    pub fn set_auto_start(enabled: bool) -> Result<()> {
        SeelenUI::set_auto_start(enabled)
    }

    pub fn get_auto_start_status() -> Result<bool> {
        SeelenUI::is_auto_start_enabled()
    }

    // used to request icon extraction
    pub fn get_icon(path: Option<PathBuf>, umid: Option<String>) -> Result<()> {
        if let Some(umid) = umid {
            request_icon_extraction_from_umid(&umid.into());
        }
        if let Some(path) = path {
            request_icon_extraction_from_file(&path);
        }
        Ok(())
    }

    pub async fn check_for_updates() -> Result<bool> {
        Ok(utils::updater::check_for_updates().await?.is_some())
    }

    pub async fn install_last_available_update() -> Result<()> {
        let update = utils::updater::check_for_updates()
            .await?
            .ok_or("There is no update available")?;
        utils::updater::trace_update_intallation(update).await?;
        get_app_handle().restart();
        #[allow(unreachable_code)]
        Ok(())
    }

    pub async fn get_native_start_menu() -> Result<StartMenuLayout> {
        let output_path = SEELEN_COMMON.app_cache_dir().join("start-layout.json");
        let output_path_str = output_path.to_string_lossy().to_string();

        let script = PwshScript::new(format!("Export-StartLayout -Path '{}'", output_path_str))
            .inline_command();
        script.execute().await?;

        let file = std::fs::File::open(&output_path)?;
        let mut layout: StartMenuLayout = serde_json::from_reader(file)?;

        for item in &mut layout.pinned_list {
            if let StartMenuLayoutItem::DesktopAppLink(path) = item {
                let source = WindowsString::from_str(path);
                let expanded = WindowsApi::resolve_environment_variables(&source)?;
                *item = StartMenuLayoutItem::DesktopAppLink(expanded.to_string());
            }
        }

        Ok(layout)
    }

    pub async fn request_to_user_input_shortcut(
        window: WebviewWindow,
        callback_event: String,
    ) -> Result<()> {
        ServiceIpc::send(SvcAction::StartShortcutRegistration).await?;

        let mut data = REG_SHORTCUT_DATA.lock();
        data.response_view_label = Some(window.label().to_string());
        data.response_event = Some(callback_event);
        Ok(())
    }
}
