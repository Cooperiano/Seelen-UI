//! Seelen owns lifecycle; Stats still owns telemetry and Touchpad owns input.
use super::lifecycle_policy::{self, Action};
use crate::{error::Result, utils::constants::SEELEN_COMMON, windows_api::WindowsApi};
use serde::{Deserialize, Serialize};
use std::{collections::BTreeMap, os::windows::process::CommandExt, path::PathBuf, process::Command,
    sync::{LazyLock, Mutex, atomic::{AtomicBool, Ordering}}, time::{Duration, Instant}};
use sysinfo::{System, ProcessesToUpdate, ProcessRefreshKind, UpdateKind};
use windows::Win32::System::RemoteDesktop::ProcessIdToSessionId;
use winreg::{RegKey, enums::{HKEY_CURRENT_USER, KEY_READ, KEY_WRITE}};

const RUN_KEY: &str = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
const RUN_VALUE: &str = "Stats Desktop";
static MANAGER: LazyLock<SuiteManager> = LazyLock::new(|| SuiteManager { gate: Mutex::new(()), errors: Mutex::new(BTreeMap::new()) });
pub static EXIT_READY: AtomicBool = AtomicBool::new(false);
static EXIT_PENDING: AtomicBool = AtomicBool::new(false);

pub struct SuiteManager {
    gate: Mutex<()>,
    errors: Mutex<BTreeMap<String, String>>,
}
impl SuiteManager {
    pub fn instance() -> &'static Self { &MANAGER }

    pub fn tracking(&self, change: Option<(serde_json::Value,serde_json::Value,bool)>) -> Result<serde_json::Value> {
        let _guard = self.gate.lock().map_err(|_| "Component manager unavailable")?;
        let path = component_path("touchpad")?;
        // Older packages do not have this early-exit diagnostic path. Never send
        // unknown arguments that might start another input receiver instead.
        let package = path.parent().and_then(|p|p.file_name()).and_then(|p|p.to_str()).ok_or("Invalid touchpad package")?;
        let version: Vec<u32> = package.split('_').nth(1).unwrap_or("").split('.').filter_map(|v|v.parse().ok()).collect();
        if version.len()!=4 || version.as_slice() < [2,2,0,48].as_slice() { return Err("Touchpad 48 or newer is required for central tracking controls".into()); }
        let nonce=uuid::Uuid::new_v4().simple().to_string();
        let mut command=Command::new(&path);
        if let Some((expected,desired,confirmed))=change {
            let values = |value:&serde_json::Value| -> Result<(u64,bool)> {
                let speed=value["Speed"].as_u64().filter(|v|(1..=20).contains(v)).ok_or("Invalid tracking speed")?;
                Ok((speed,value["Acceleration"].as_bool().ok_or("Invalid acceleration choice")?))
            };
            let (old_speed,old_acceleration)=values(&expected)?;
            let (speed,acceleration)=values(&desired)?;
            command.args(["--suite-tracking-apply".to_owned(),nonce.clone(),old_speed.to_string(),old_acceleration.to_string(),speed.to_string(),acceleration.to_string(),confirmed.to_string()]);
        } else { command.args(["--suite-tracking-read",&nonce]); }
        let mut child=command.creation_flags(0x08000000).spawn()?;
        let deadline=Instant::now()+Duration::from_secs(8);
        let status=loop {
            if let Some(status)=child.try_wait()? { break status; }
            if Instant::now()>=deadline {
                // Only this new diagnostic child can be killed, never the input engine.
                let _=child.kill();let _=child.wait();return Err("Tracking diagnostic timed out".into());
            }
            std::thread::sleep(Duration::from_millis(50));
        };
        let receipt=super::preferences_store::read(&component_preferences_path("touchpad")?.with_file_name("suite-tracking.json")).map_err(crate::error::AppError::from)?.data;
        if receipt["RequestId"].as_str()!=Some(nonce.as_str()) { return Err("Tracking response does not belong to this request".into()); }
        if !status.success() || receipt["Result"]["Success"]!=true {
            return Err(format!("Tracking change was not confirmed: {}",receipt["Result"]["Error"]).into());
        }
        let data=&receipt["Result"]["Observed"];
        if !data.is_object() { return Err("Tracking readback unavailable".into()); }
        Ok(serde_json::json!({"data":data,"revision":format!("{}:{}",data["Speed"],data["Acceleration"]),
            "enabled":preferences()?.enabled("touchpad"),"running":is_running(&path)}))
    }

    pub fn read_component_preferences(&self, id: &str) -> Result<serde_json::Value> {
        let _guard = self.gate.lock().map_err(|_| "Component manager unavailable")?;
        let document = super::preferences_store::read(&component_preferences_path(id)?).map_err(crate::error::AppError::from)?;
        Ok(serde_json::json!({"data":document.data,"revision":document.revision,
            "enabled":preferences()?.enabled(id),"running":is_running(&component_path(id)?)}))
    }

    pub fn save_component_preferences(&self, id: &str, revision: &str, patch: &serde_json::Value) -> Result<serde_json::Value> {
        let _guard = self.gate.lock().map_err(|_| "Component manager unavailable")?;
        let path = component_preferences_path(id)?;
        let mut runtime = PreferencesRuntime { id, restart: id == "touchpad" && is_running(&component_path(id)?) };
        let document = super::preferences_store::commit(&path, id, revision, patch, &mut runtime,
            |path, bytes| crate::utils::atomic_write_file(path, bytes).map_err(|e|e.to_string()))
            .map_err(crate::error::AppError::from)?;
        Ok(serde_json::json!({"data":document.data,"revision":document.revision,
            "enabled":preferences()?.enabled(id),"running":is_running(&component_path(id)?)}))
    }

    pub fn state(&self) -> Result<serde_json::Value> {
        let _guard = self.gate.lock().map_err(|_| "Component manager unavailable")?;
        let preferences = preferences()?;
        let mut rows = Vec::new();
        for id in ["stats", "touchpad"] {
            let path = component_path(id).ok();
            rows.push(serde_json::json!({"id":id, "enabled":preferences.enabled(id),
                "available":path.as_ref().is_some_and(|p| p.is_file()),
                "running":path.as_ref().is_some_and(|p| is_running(p)),
                "error":self.errors.lock().map_err(|_| "Status unavailable")?.get(id).cloned()}));
        }
        Ok(serde_json::json!({"components":rows,"autostart":autostart_enabled()?,"taskbar":super::taskbar_guard::status()}))
    }

    pub fn set_enabled(&self, id: &str, enabled: bool) -> Result<()> {
        if !lifecycle_policy::known_component(id) { return Err("Unknown suite component".into()); }
        let _guard = self.gate.lock().map_err(|_| "Component manager unavailable")?;
        let mut preferences = preferences()?;
        let before = preferences.enabled(id);
        let result = (|| {
            reconcile_component(id, enabled)?;
            preferences.set(id, enabled);
            if let Err(error) = crate::utils::atomic_write_file(&preferences_path(), &serde_json::to_vec_pretty(&preferences)?) {
                // Restore the previous desired runtime state if persisting failed.
                let rollback = reconcile_component(id, before);
                return Err(format!("Saving component preference failed: {error}; runtime rollback: {rollback:?}").into());
            }
            Ok(())
        })();
        self.record(id, &result);
        result
    }

    pub fn start_enabled(&self) -> Result<()> {
        let _guard = self.gate.lock().map_err(|_| "Component manager unavailable")?;
        let preferences = preferences()?;
        for id in ["stats", "touchpad"] {
            // No periodic supervision: an intentionally stopped optional component stays stopped.
            if preferences.enabled(id) {
                let result = reconcile_component(id, true);
                self.record(id, &result);
            }
        }
        Ok(())
    }

    pub fn stop_all(&self) -> Result<()> {
        let _guard = self.gate.lock().map_err(|_| "Component manager unavailable")?;
        let mut failures = Vec::new();
        for id in ["touchpad", "stats"] {
            let result = reconcile_component(id, false);
            self.record(id, &result);
            if let Err(error) = result { failures.push(format!("{id}: {error}")); }
        }
        if failures.is_empty() { Ok(()) } else { Err(failures.join("; ").into()) }
    }

    fn record(&self, id: &str, result: &Result<()>) {
        if let Ok(mut errors) = self.errors.lock() {
            match result { Ok(()) => { errors.remove(id); }, Err(error) => { errors.insert(id.into(), error.to_string()); } }
        }
    }
}

struct PreferencesRuntime<'a> { id: &'a str, restart: bool }
impl super::preferences_store::Runtime for PreferencesRuntime<'_> {
    fn stop(&mut self) -> std::result::Result<(),String> {
        if self.restart { reconcile_component(self.id, false).map_err(|e|e.to_string()) } else { Ok(()) }
    }
    fn resume(&mut self) -> std::result::Result<(),String> {
        if self.restart { reconcile_component(self.id, true).map_err(|e|e.to_string()) } else { Ok(()) }
    }
}

fn component_preferences_path(id: &str) -> Result<PathBuf> {
    use tauri::Manager;
    let local = crate::app::get_app_handle().path().local_data_dir()?;
    match id {
        "stats" => Ok(local.join("Stats").join("stats-settings.json")),
        "touchpad" => Ok(local.join("Packages").join("50931ClmentGrennerat.ThreeFingersDragOnWindows_cvkce2k9t2r60")
            .join("LocalState").join("preferences.json")),
        _ => Err("Unknown suite component".into()),
    }
}

#[derive(Serialize, Deserialize)]
#[serde(default)]
struct Preferences { stats: bool, touchpad: bool }
impl Default for Preferences {
    fn default() -> Self { Self { stats: true, touchpad: true } }
}
impl Preferences {
    fn enabled(&self, id: &str) -> bool { match id { "stats" => self.stats, "touchpad" => self.touchpad, _ => false } }
    fn set(&mut self, id: &str, value: bool) { match id { "stats" => self.stats = value, "touchpad" => self.touchpad = value, _ => {} } }
}

pub fn stats_enabled() -> bool { preferences().is_ok_and(|p| p.stats) }
fn preferences_path() -> PathBuf { SEELEN_COMMON.app_data_dir().join("desktop-suite-components.json") }
fn preferences() -> Result<Preferences> {
    match std::fs::read(preferences_path()) {
        Ok(bytes) => Ok(serde_json::from_slice(&bytes)?),
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => Ok(Preferences::default()),
        Err(error) => Err(error.into()),
    }
}

pub fn component_path(id: &str) -> Result<PathBuf> {
    if !lifecycle_policy::known_component(id) { return Err("Unknown suite component".into()); }
    let directory = std::env::current_exe()?.parent().ok_or("Host directory unavailable")?.to_owned();
    let manifest: BTreeMap<String, PathBuf> = serde_json::from_slice(&std::fs::read(directory.join("suite-components.json"))?)?;
    let path = manifest.get(id).ok_or("Component not installed")?;
    let expected = if id == "stats" { "Stats.exe" } else { "ThreeFingerDragOnWindows.exe" };
    if !path.is_absolute() || !path.file_name().is_some_and(|name| name.eq_ignore_ascii_case(expected)) {
        return Err("Invalid installed component path".into());
    }
    Ok(path.clone())
}

fn is_running(path: &std::path::Path) -> bool {
    let mut system = System::new();
    system.refresh_processes_specifics(ProcessesToUpdate::All, true,
        ProcessRefreshKind::nothing().with_exe(UpdateKind::Always));
    let session = WindowsApi::current_session_id();
    system.processes().iter().any(|(pid, process)| {
        let mut observed = u32::MAX;
        unsafe { ProcessIdToSessionId(pid.as_u32(), &mut observed) }.is_ok()
            && observed == session && process.exe().is_some_and(|exe| exe == path)
    })
}

fn spawn_component(id: &str, argument: &str) -> Result<()> {
    let path = component_path(id)?;
    if !path.is_file() { return Err("Installed component executable is missing".into()); }
    Command::new(&path).arg(argument).current_dir(path.parent().ok_or("Component directory missing")?)
        .creation_flags(0x08000000).spawn()?;
    Ok(())
}

fn reconcile_component(id: &str, enabled: bool) -> Result<()> {
    let path = component_path(id)?;
    match lifecycle_policy::reconcile(enabled, is_running(&path)) {
        Action::None => return Ok(()),
        Action::Start => spawn_component(id, "--background")?,
        Action::Stop => spawn_component(id, "--exit")?,
    }
    let deadline = Instant::now() + Duration::from_secs(12);
    while Instant::now() < deadline {
        if is_running(&path) == enabled { return Ok(()); }
        std::thread::sleep(Duration::from_millis(150));
    }
    // Never kill input/collector processes to make a success indicator pass.
    Err(format!("{id}: normal {} was not confirmed; no force termination used", if enabled { "startup" } else { "shutdown" }).into())
}

fn startup_command() -> Result<String> { Ok(format!("\"{}\"", std::env::current_exe()?.display())) }
pub fn autostart_enabled() -> Result<bool> {
    let key = RegKey::predef(HKEY_CURRENT_USER).open_subkey_with_flags(RUN_KEY, KEY_READ)?;
    Ok(key.get_value::<String, _>(RUN_VALUE).ok().as_ref() == Some(&startup_command()?))
}
pub fn set_autostart(enabled: bool) -> Result<()> {
    let key = RegKey::predef(HKEY_CURRENT_USER).open_subkey_with_flags(RUN_KEY, KEY_READ | KEY_WRITE)?;
    if enabled { key.set_value(RUN_VALUE, &startup_command()?)?; }
    else { match key.delete_value(RUN_VALUE) { Ok(()) => {}, Err(e) if e.kind() == std::io::ErrorKind::NotFound => {}, Err(e) => return Err(e.into()) } }
    if autostart_enabled()? != enabled { return Err("Sign-in startup verification failed".into()); }
    Ok(())
}

pub fn request_exit() {
    if EXIT_PENDING.swap(true, Ordering::SeqCst) { return; }
    std::thread::spawn(|| {
        match SuiteManager::instance().stop_all() {
            Ok(()) => { EXIT_READY.store(true, Ordering::SeqCst); crate::app::get_app_handle().exit(0); },
            Err(error) => { log::error!("Suite exit aborted: {error}"); EXIT_PENDING.store(false, Ordering::SeqCst); }
        }
    });
}
