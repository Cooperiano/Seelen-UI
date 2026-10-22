//! Read-only port of the user-accepted Brimbar 0.6.22 Pinyin compatibility adapter.
//! No hooks, simulated keys, text capture, profile activation or IME setters.
use super::contract::stable_mode;
use windows::Win32::System::Threading::GetCurrentProcessId;
use windows::Win32::{
    Foundation::{LPARAM, WPARAM},
    UI::{
        Input::{Ime::ImmGetDefaultIMEWnd, KeyboardAndMouse::GetKeyboardLayout},
        WindowsAndMessaging::{
            GA_ROOT, GUITHREADINFO, GetAncestor, GetClassNameW, GetForegroundWindow,
            GetGUIThreadInfo, GetWindowThreadProcessId, SMTO_ABORTIFHUNG, SMTO_ERRORONEXIT,
            SendMessageTimeoutW, WM_IME_CONTROL,
        },
    },
};
use winreg::{
    RegKey,
    enums::{HKEY_CURRENT_USER, KEY_READ},
};

fn sole_pinyin_profile() -> Option<()> {
    let root = RegKey::predef(HKEY_CURRENT_USER)
        .open_subkey_with_flags(
            r"Software\Microsoft\CTF\SortOrder\AssemblyItem\0x00000804",
            KEY_READ,
        )
        .ok()?;
    let mut count = 0;
    for (index, name) in root.enum_keys().enumerate() {
        if index >= 64 {
            return None;
        }
        let group = root.open_subkey_with_flags(name.ok()?, KEY_READ).ok()?;
        for (index, name) in group.enum_keys().enumerate() {
            count += 1;
            if index >= 64 || count > 1 {
                return None;
            }
            let item = group.open_subkey_with_flags(name.ok()?, KEY_READ).ok()?;
            let clsid: String = item.get_value("CLSID").ok()?;
            let profile: String = item.get_value("Profile").ok()?;
            if !clsid.eq_ignore_ascii_case("{81D4E9C9-1D3B-41BC-9E6C-4B40BF79E35E}")
                || !profile.eq_ignore_ascii_case("{FA550B04-5AD7-411F-A5AC-CA038EC515D7}")
            {
                return None;
            }
        }
    }
    (count == 1).then_some(())
}

pub fn indicator() -> String {
    // SAFETY: only OS-owned handles and bounded output buffers; no dereferencing HWNDs.
    unsafe {
        let app = GetForegroundWindow();
        let mut pid = 0;
        let tid = GetWindowThreadProcessId(app, Some(&mut pid));
        if app.is_invalid() || tid == 0 || pid == 0 {
            return "—".into();
        }
        // Opening our own control center is not a user input-language change.
        // Empty means retain the last external application's label in the toolbar.
        if pid == GetCurrentProcessId() {
            return String::new();
        }
        let hkl = GetKeyboardLayout(tid);
        let lang = (hkl.0 as usize & 0xffff) as u16;
        let fallback = match lang & 0x3ff {
            4 => "ZH",
            9 => "EN",
            17 => "JA",
            18 => "KO",
            _ => "⌨",
        };
        if hkl.0 as usize != 0x08040804 || sole_pinyin_profile().is_none() {
            return fallback.into();
        }
        let get_focus = || {
            let mut info = GUITHREADINFO {
                cbSize: std::mem::size_of::<GUITHREADINFO>() as u32,
                ..Default::default()
            };
            GetGUIThreadInfo(tid, &mut info)
                .ok()
                .map(|_| info.hwndFocus)
        };
        let Some(focus) = get_focus().filter(|h| !h.is_invalid()) else {
            return fallback.into();
        };
        let ime = ImmGetDefaultIMEWnd(focus);
        let current = || {
            let mut owner = 0;
            let mut focus_owner = 0;
            let mut app_owner = 0;
            let mut name = [0u16; 32];
            let length = GetClassNameW(ime, &mut name);
            !ime.is_invalid()
                && GetForegroundWindow() == app
                && GetWindowThreadProcessId(app, Some(&mut app_owner)) == tid
                && app_owner == pid
                && GetAncestor(focus, GA_ROOT) == GetAncestor(app, GA_ROOT)
                && GetKeyboardLayout(tid) == hkl
                && get_focus() == Some(focus)
                && ImmGetDefaultIMEWnd(focus) == ime
                && GetWindowThreadProcessId(focus, Some(&mut focus_owner)) == tid
                && focus_owner == pid
                && GetWindowThreadProcessId(ime, Some(&mut owner)) == tid
                && owner == pid
                && length == 3
                && String::from_utf16_lossy(&name[..3]).eq_ignore_ascii_case("IME")
        };
        let query = |command| {
            let mut result = 0;
            let sent = SendMessageTimeoutW(
                ime,
                WM_IME_CONTROL,
                WPARAM(command),
                LPARAM(0),
                SMTO_ABORTIFHUNG | SMTO_ERRORONEXIT,
                15,
                Some(&mut result),
            );
            (sent.0 != 0).then_some(result)
        };
        let read = || Some((query(5)?, query(1)?));
        if !current() {
            return fallback.into();
        }
        let first = read();
        if !current() {
            return fallback.into();
        }
        let second = read();
        match stable_mode(current(), first, second) {
            Some(true) => "中".into(),
            Some(false) => "英".into(),
            None => fallback.into(),
        }
    }
}
