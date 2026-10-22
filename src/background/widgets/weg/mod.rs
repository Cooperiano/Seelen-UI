pub mod cli;
pub mod handler;
pub mod hook;
pub(crate) mod identity;
pub mod native_taskbar;
pub(crate) mod native_order;
mod native_pins;

use slu_ipc::messages::SvcAction;

use crate::{
    cli::ServicePipe, error::Result, state::application::FULL_STATE, windows_api::monitor::Monitor,
};

pub static TASKBAR_CLASS: [&str; 2] = ["Shell_TrayWnd", "Shell_SecondaryTrayWnd"];

pub struct SeelenWeg {}

impl SeelenWeg {
    pub fn native_shell_takeover_enabled() -> bool {
        !slu_ipc::identity::SUITE_BUILD || cfg!(feature = "native-shell-takeover")
    }
    #[allow(dead_code)]
    pub fn get_weg_size_on_monitor(monitor: &Monitor) -> Result<i32> {
        let state = FULL_STATE.load();
        let settings: &seelen_core::state::SeelenWegSettings = &state.settings.by_widget.weg;
        let total_size = (settings.total_size() as f64 * monitor.scale_factor()?) as i32;
        Ok(total_size)
    }

    // ====================
    // TASKBAR HIDDEN LOGIC
    // ====================
    //
    // The actual hide/restore of the native taskbar is performed by the service
    // (see src/service/shutdown.rs), which owns the "was it hidden" flag so it can
    // safely restore on shutdown even if the main app crashes. This app only
    // requests the action via IPC.

    pub fn hide_native_taskbar() {
        if !Self::native_shell_takeover_enabled() {
            return;
        }
        let _ = ServicePipe::request(SvcAction::HideNativeTaskbar);
    }

    pub fn restore_native_taskbar() -> Result<()> {
        if !Self::native_shell_takeover_enabled() {
            return Ok(());
        }
        ServicePipe::request(SvcAction::RestoreNativeTaskbar)
    }
}
