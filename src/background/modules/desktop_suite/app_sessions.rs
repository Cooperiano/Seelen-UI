//! Track only processes previously observed owning an application window.
//! A retained process handle, not a recycled PID/path, defines a live session.
use std::{collections::BTreeMap, sync::{LazyLock, Mutex}};
use seelen_core::system_state::UserAppWindow;
use windows::Win32::{Foundation::HANDLE, System::Threading::GetExitCodeProcess};
use windows_core::Owned;
use crate::{modules::apps::application::USER_APPS_MANAGER, windows_api::process::Process};

struct Session { process: Owned<HANDLE>, window: UserAppWindow }
// Windows process handles are valid across threads; access is serialized by SESSIONS.
unsafe impl Send for Session {}
static SESSIONS: LazyLock<Mutex<BTreeMap<(u32,String),Session>>> = LazyLock::new(||Mutex::new(BTreeMap::new()));

fn alive(session:&Session)->bool {
    let mut code=0;
    unsafe {GetExitCodeProcess(*session.process,&mut code)}.is_ok() && code==259
}

pub fn snapshot()->Vec<UserAppWindow> {
    let windows=USER_APPS_MANAGER.interactable_windows.to_vec();
    let Ok(mut sessions)=SESSIONS.lock() else {return Vec::new()};
    sessions.retain(|_,session|alive(session));
    for window in &windows {
        // Unknown executable identities remain window-scoped, never a persistent background app.
        if window.process.path.is_none() {continue;}
        let key=(window.process.id,window.umid.clone().unwrap_or_default());
        if let Some(session)=sessions.get_mut(&key) {session.window=window.clone();continue;}
        if sessions.len()>=4096 {break;}
        if let Ok(process)=Process::from_id(window.process.id).open_limited_handle() {
            sessions.insert(key,Session{process,window:window.clone()});
        }
    }
    // Shared host processes cannot prove that a particular PWA/profile is still running.
    sessions.values().filter(|s| {
        windows.iter().any(|w|w.process.id==s.window.process.id && w.umid==s.window.umid)
            || !windows.iter().any(|w|w.process.id==s.window.process.id && w.umid!=s.window.umid)
    }).map(|s|s.window.clone()).collect()
}
