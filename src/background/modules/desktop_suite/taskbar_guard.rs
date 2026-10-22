//! User-level shell lease. This executable's early guard mode restores the shell
//! independently of the UI process; it never starts collectors, input or services.
use std::{path::PathBuf, process::{Child,Command}, os::windows::process::CommandExt,
    sync::{LazyLock,Mutex}, time::{SystemTime,UNIX_EPOCH,Duration}};
use serde::{Serialize,Deserialize};
use windows::Win32::{Foundation::{HWND,LPARAM,RECT},
    Graphics::Gdi::{MonitorFromWindow,MONITOR_DEFAULTTONEAREST},
    System::Threading::GetExitCodeProcess,
    UI::{Shell::{APPBARDATA,SHAppBarMessage,ABM_GETSTATE,ABM_SETSTATE},
        WindowsAndMessaging::{GetWindowRect,IsWindowVisible,ShowWindow,SW_HIDE,SW_SHOWNOACTIVATE}}};
use crate::{error::Result,utils::{atomic_write_file,constants::SEELEN_COMMON},
    windows_api::{WindowsApi,WindowEnumerator,process::Process}};

#[derive(Clone,Serialize,Deserialize)] struct Dock { hwnd:isize, at:u64, ready:bool }
#[derive(Serialize,Deserialize)] struct Lease { pid:u32, nonce:String, #[serde(default)] enabled:bool, docks:Vec<Dock> }
#[derive(Serialize,Deserialize)] struct Receipt { pid:u32, nonce:String, original_state:u32, hidden:bool, bars:Vec<(isize,bool)> }
#[derive(Default)] struct GuardState { docks:Vec<Dock>, child:Option<Child> }
static STATE:LazyLock<Mutex<GuardState>>=LazyLock::new(||Mutex::new(GuardState::default()));
static NONCE:LazyLock<String>=LazyLock::new(||uuid::Uuid::new_v4().simple().to_string());

fn now()->u64 {SystemTime::now().duration_since(UNIX_EPOCH).unwrap_or_default().as_millis() as u64}
fn configured()->bool {
    let config=crate::state::application::FULL_STATE.load();
    let mode=match config.settings.by_widget.weg.hide_mode {
        seelen_core::state::HideMode::Never=>"Never",
        seelen_core::state::HideMode::Always=>"Always",
        seelen_core::state::HideMode::OnOverlap=>"OnOverlap",
    };
    super::taskbar_policy::configured(config.settings.by_widget.weg.enabled,mode)
}
pub fn status()->serde_json::Value {
    let dir=SEELEN_COMMON.app_data_dir();
    let active=std::fs::read(dir.join("taskbar-guard-receipt.json")).ok()
        .and_then(|v|serde_json::from_slice::<Receipt>(&v).ok())
        .is_some_and(|r|r.pid==std::process::id() && r.nonce==*NONCE && r.hidden);
    serde_json::json!({"enabled":configured(),"active":active})
}
pub fn set_enabled(enabled:bool)->Result<()> {
    if !slu_ipc::identity::SUITE_BUILD {return Err("Suite only".into());}
    // Compatibility entry point: change the same Dock switch used by settings,
    // not a second replacement preference. The guard still waits for real UI health.
    crate::state::application::FULL_STATE.rcu(|state| {
        let mut state=state.cloned();
        state.settings.by_widget.weg.enabled=enabled;
        state
    });
    crate::state::application::FULL_STATE.load().write_settings()?;
    if !enabled {recover_receipt(true);}
    Ok(())
}
pub fn heartbeat(window:&tauri::WebviewWindow,ready:bool)->Result<()> {
    if !slu_ipc::identity::SUITE_BUILD {return Ok(());}
    let label=crate::widgets::webview::WidgetWebviewLabel::try_from_raw(window.label())?;
    if label.widget_id!=seelen_core::resource::WidgetId::known_weg() {return Err("Only taskbar can renew its lease".into());}
    let hwnd=window.hwnd()?.0 as isize;
    let enabled=configured();
    let ready=ready && enabled;
    let mut state=STATE.lock().map_err(|_|"Taskbar lease unavailable")?;
    state.docks.retain(|d|d.hwnd!=hwnd&&now().saturating_sub(d.at)<5000);
    state.docks.push(Dock{hwnd,at:now(),ready});
    let lease=Lease{pid:std::process::id(),nonce:NONCE.clone(),enabled,docks:state.docks.clone()};
    atomic_write_file(&SEELEN_COMMON.app_data_dir().join("taskbar-lease.json"),&serde_json::to_vec(&lease)?)?;
    if state.child.as_mut().is_some_and(|c|c.try_wait().ok().flatten().is_none()) {return Ok(());}
    recover_receipt(true);
    if ready {
        state.child=Some(Command::new(std::env::current_exe()?).args(["--suite-taskbar-guard",&std::process::id().to_string(),&NONCE])
            .creation_flags(0x08000000).spawn()?);
    }
    Ok(())
}
fn taskbars()->Vec<HWND> {
    let mut result=Vec::new();
    let _=WindowEnumerator::new().for_each(|window| {
        let hwnd=window.hwnd();
        let class=window.class();
        if class=="Shell_TrayWnd"||class=="Shell_SecondaryTrayWnd" {result.push(hwnd);}
    });
    result
}
fn native_state()->u32 {
    let mut data=APPBARDATA{cbSize:std::mem::size_of::<APPBARDATA>() as u32,..Default::default()};
    unsafe {SHAppBarMessage(ABM_GETSTATE,&mut data) as u32}
}
fn set_native_state(value:u32) {
    let mut data=APPBARDATA{cbSize:std::mem::size_of::<APPBARDATA>() as u32,lParam:LPARAM(value as isize),..Default::default()};
    unsafe {SHAppBarMessage(ABM_SETSTATE,&mut data);}
}
struct Restore { dir:PathBuf,receipt:Receipt }
impl Drop for Restore {
    fn drop(&mut self) {
        set_native_state(self.receipt.original_state);
        for hwnd in taskbars() {
            let original=self.receipt.bars.iter().find(|(id,_)|*id==hwnd.0 as isize);
            if super::taskbar_policy::should_restore_bar(self.receipt.original_state,original.map(|(_,visible)|*visible)) {
                unsafe {let _=ShowWindow(hwnd,SW_SHOWNOACTIVATE);}
            }
        }
        self.receipt.hidden=false;
        let _=atomic_write_file(&self.dir.join("taskbar-guard-receipt.json"),&serde_json::to_vec(&self.receipt).unwrap_or_default());
    }
}
pub fn recover_receipt(force:bool) {
    let dir=SEELEN_COMMON.app_data_dir();
    let receipt=std::fs::read(dir.join("taskbar-guard-receipt.json")).ok().filter(|v|v.len()<16384)
        .and_then(|v|serde_json::from_slice::<Receipt>(&v).ok());
    let Some(receipt)=receipt.filter(|r|r.hidden&&r.original_state<=3) else {return};
    if !force {
        if let Ok(process)=Process::from_id(receipt.pid).open_limited_handle() {
            let mut code=0;
            if unsafe {GetExitCodeProcess(*process,&mut code)}.is_ok()&&code==259 {return;}
        }
    }
    drop(Restore{dir:dir.to_owned(),receipt});
}
fn run_guard(pid:u32,nonce:&str)->Result<()> {
    let own=std::env::current_exe()?;
    if Process::from_id(pid).program_path()?!=own {return Err("Parent identity differs".into());}
    let parent=Process::from_id(pid).open_limited_handle()?;
    let dir=PathBuf::from(std::env::var_os("APPDATA").ok_or("No app data")?).join("com.cooperiano.stats-desktop-dev");
    let mut restore:Option<Restore>=None;
    loop {
        let mut code=0;
        let alive=unsafe {GetExitCodeProcess(*parent,&mut code)}.is_ok()&&code==259;
        let lease=std::fs::read(dir.join("taskbar-lease.json")).ok().filter(|v|v.len()<16384)
            .and_then(|v|serde_json::from_slice::<Lease>(&v).ok());
        let bars=taskbars();
        let ready=lease.as_ref().is_some_and(|l|l.pid==pid&&l.nonce==nonce&&!bars.is_empty()&&bars.iter().all(|bar| {
            l.docks.iter().any(|d| {
                let dock=HWND(d.hwnd as *mut _);
                let mut rect=RECT::default();
                d.ready&&now().saturating_sub(d.at)<5000
                    && WindowsApi::window_thread_process_id(dock).0==pid
                    && unsafe {IsWindowVisible(dock)}.as_bool()
                    && unsafe {GetWindowRect(dock,&mut rect)}.is_ok()&&rect.right-rect.left>100&&rect.bottom-rect.top>20
                    && unsafe {MonitorFromWindow(*bar,MONITOR_DEFAULTTONEAREST)==MonitorFromWindow(dock,MONITOR_DEFAULTTONEAREST)}
            })
        }));
        if !super::taskbar_policy::may_hide(lease.as_ref().is_some_and(|l|l.enabled),alive,ready,0) {break;}
        if restore.is_none() {
            let receipt=Receipt{pid,nonce:nonce.into(),original_state:native_state(),hidden:true,
                bars:bars.iter().map(|h|(h.0 as isize,unsafe{IsWindowVisible(*h)}.as_bool())).collect()};
            // Durable restoration state is recorded before touching the native shell.
            atomic_write_file(&dir.join("taskbar-guard-receipt.json"),&serde_json::to_vec(&receipt)?)?;
            restore=Some(Restore{dir:dir.clone(),receipt});
        }
        set_native_state(restore.as_ref().unwrap().receipt.original_state|1);
        for bar in bars {unsafe {let _=ShowWindow(bar,SW_HIDE);}}
        std::thread::sleep(Duration::from_millis(200));
    }
    drop(restore);
    Ok(())
}
/// Called before logger/Tauri/IPC initialization. The guard is never a second host.
pub fn early_guard()->Option<u8> {
    let args:Vec<String>=std::env::args().collect();
    if args.get(1).map(String::as_str)!=Some("--suite-taskbar-guard") {return None;}
    if !slu_ipc::identity::SUITE_BUILD||args.len()!=4 {return Some(2);}
    let Ok(pid)=args[2].parse::<u32>() else {return Some(2)};
    if uuid::Uuid::parse_str(&args[3]).is_err() {return Some(2);}
    Some(if run_guard(pid,&args[3]).is_ok(){0}else{1})
}
