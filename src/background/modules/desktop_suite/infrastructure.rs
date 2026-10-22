use super::{
    application::{self, Mapping},
    contract::{METRIC_COUNT, MODULES},
    input, lifecycle,
};
use crate::{
    app::get_app_handle,
    error::Result,
    widgets::permissions::{WidgetPerm, request_widget_permission},
};
use std::sync::{
    LazyLock, Mutex,
    atomic::{Ordering, fence},
};
use tauri::Manager;
use windows::{
    Win32::{
        Foundation::{CloseHandle, INVALID_HANDLE_VALUE},
        System::{
            Memory::{CreateFileMappingW, FILE_MAP_WRITE, MapViewOfFile, PAGE_READWRITE},
            Threading::{EVENT_MODIFY_STATE, OpenEventW, SetEvent},
        },
    },
    core::w,
};

static ANCHORS: LazyLock<Mutex<Option<Mapping>>> = LazyLock::new(|| Mutex::new(None));

fn metrics() -> Option<serde_json::Value> {
    let snapshot = application::metrics()?;
    Some(
        serde_json::json!({"sequence": snapshot.sequence, "enabledMask": snapshot.enabled_mask,
        "values": snapshot.values, "order": snapshot.order, "styles": snapshot.styles, "colors": snapshot.colors}),
    )
}

impl crate::tauri_handlers::Handlers {
    pub fn set_desktop_suite_taskbar(webview:tauri::WebviewWindow,enabled:bool)->Result<()> {
        require_suite_settings(&webview)?;
        super::taskbar_guard::set_enabled(enabled)
    }
    pub fn desktop_suite_taskbar_heartbeat(webview:tauri::WebviewWindow,ready:bool)->Result<()> {
        super::taskbar_guard::heartbeat(&webview,ready)
    }
    pub async fn get_desktop_suite_tracking(webview: tauri::WebviewWindow) -> Result<serde_json::Value> {
        require_suite_settings(&webview)?;
        tokio::task::spawn_blocking(|| lifecycle::SuiteManager::instance().tracking(None)).await?
    }
    pub async fn save_desktop_suite_tracking(webview: tauri::WebviewWindow, expected: serde_json::Value, desired: serde_json::Value, confirmed: bool) -> Result<serde_json::Value> {
        require_suite_settings(&webview)?;
        tokio::task::spawn_blocking(move || lifecycle::SuiteManager::instance().tracking(Some((expected,desired,confirmed)))).await?
    }
    pub async fn get_desktop_suite_preferences(webview: tauri::WebviewWindow, module: String) -> Result<serde_json::Value> {
        require_suite_settings(&webview)?;
        tokio::task::spawn_blocking(move || lifecycle::SuiteManager::instance().read_component_preferences(&module)).await?
    }
    pub async fn save_desktop_suite_preferences(webview: tauri::WebviewWindow, module: String, revision: String, patch: serde_json::Value) -> Result<serde_json::Value> {
        require_suite_settings(&webview)?;
        tokio::task::spawn_blocking(move || lifecycle::SuiteManager::instance().save_component_preferences(&module, &revision, &patch)).await?
    }
    pub fn get_desktop_suite_metrics() -> Option<serde_json::Value> {
        (slu_ipc::identity::SUITE_BUILD && lifecycle::stats_enabled()).then(metrics).flatten()
    }
    pub async fn get_desktop_suite_state() -> Result<serde_json::Value> {
        if !slu_ipc::identity::SUITE_BUILD { return Err("Suite command unavailable".into()); }
        tokio::task::spawn_blocking(|| lifecycle::SuiteManager::instance().state()).await?
    }
    pub async fn set_desktop_suite_component(webview: tauri::WebviewWindow, module: String, enabled: bool) -> Result<()> {
        request_widget_permission(&webview, WidgetPerm::Run)?;
        if !slu_ipc::identity::SUITE_BUILD { return Err("Suite command unavailable".into()); }
        tokio::task::spawn_blocking(move || lifecycle::SuiteManager::instance().set_enabled(&module, enabled)).await?
    }
    pub async fn exit_desktop_suite(webview: tauri::WebviewWindow) -> Result<()> {
        request_widget_permission(&webview, WidgetPerm::Run)?;
        if !slu_ipc::identity::SUITE_BUILD { return Err("Suite command unavailable".into()); }
        tokio::task::spawn_blocking(|| lifecycle::SuiteManager::instance().stop_all()).await??;
        lifecycle::EXIT_READY.store(true, Ordering::SeqCst);
        crate::get_tokio_handle().spawn(async {
            tokio::time::sleep(std::time::Duration::from_millis(100)).await;
            get_app_handle().exit(0);
        });
        Ok(())
    }
    pub async fn get_desktop_suite_input() -> String {
        if slu_ipc::identity::SUITE_BUILD {
            tokio::task::spawn_blocking(input::indicator)
                .await
                .unwrap_or_else(|_| "—".into())
        } else {
            String::new()
        }
    }
    pub fn is_desktop_suite() -> bool {
        slu_ipc::identity::SUITE_BUILD
    }

    /// Reuse Stats' existing open-request channel; never spawn a second collector.
    pub fn open_desktop_suite_metric(
        webview: tauri::WebviewWindow,
        id: u32,
        rect: Vec<i32>,
    ) -> Result<()> {
        request_widget_permission(&webview, WidgetPerm::Run)?;
        if !lifecycle::stats_enabled() { return Err("Stats component is disabled".into()); }
        if !slu_ipc::identity::SUITE_BUILD
            || id as usize >= METRIC_COUNT
            || rect.len() != 4
            || rect.iter().any(|n| n.abs_diff(0) > 100_000)
            || rect[2] <= rect[0]
            || rect[3] <= rect[1]
        {
            return Err("Invalid suite metric anchor".into());
        }
        // Keep the anchor map alive until app exit so Stats can position its native popup.
        let mut guard = ANCHORS.lock().map_err(|_| "Anchor lock unavailable")?;
        unsafe {
            if guard.is_none() {
                let handle = CreateFileMappingW(
                    INVALID_HANDLE_VALUE,
                    None,
                    PAGE_READWRITE,
                    0,
                    256,
                    w!("Local\\Brimbar.MetricAnchors.v1"),
                )?;
                let view = MapViewOfFile(handle, FILE_MAP_WRITE, 0, 0, 256);
                if view.Value.is_null() {
                    let _ = CloseHandle(handle);
                    return Err("Anchor map unavailable".into());
                }
                *guard = Some(Mapping { handle, view });
            }
            let map = guard.as_ref().ok_or("Anchor map unavailable")?;
            let base = map.view.Value.cast::<u8>();
            let seq = base.add(8).cast::<i64>();
            let next = (seq.read_volatile().max(0) & !1).wrapping_add(2).max(2);
            seq.write_volatile(next - 1);
            fence(Ordering::SeqCst);
            base.cast::<u32>().write_volatile(0x31414d42);
            base.add(4).cast::<u32>().write_volatile(1);
            base.add(16).cast::<u32>().write_volatile(1 << id);
            for (index, value) in rect.iter().enumerate() {
                base.add(24 + id as usize * 16 + index * 4)
                    .cast::<i32>()
                    .write_volatile(*value);
            }
            fence(Ordering::SeqCst);
            seq.write_volatile(next);
            // Fail visibly if the existing publisher is not accepting requests.
            let event = OpenEventW(EVENT_MODIFY_STATE, false, w!("Local\\Stats.OpenRequest"))?;
            let directory = get_app_handle().path().local_data_dir()?.join("Stats");
            let result = crate::utils::atomic_write_file(
                &directory.join("open-request.txt"),
                format!("module:{}", MODULES[id as usize]).as_bytes(),
            )
            .and_then(|_| SetEvent(event).map_err(Into::into));
            let _ = CloseHandle(event);
            result
        }
    }
}

fn require_suite_settings(webview: &tauri::WebviewWindow) -> Result<()> {
    let label = crate::widgets::webview::WidgetWebviewLabel::try_from_raw(webview.label())?;
    if !slu_ipc::identity::SUITE_BUILD || label.widget_id != seelen_core::resource::WidgetId::known_settings() {
        return Err("Only the primary suite settings window can access component preferences".into());
    }
    request_widget_permission(webview, WidgetPerm::ModifyConfiguration)
}
