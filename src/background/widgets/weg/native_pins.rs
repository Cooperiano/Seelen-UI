//! Read-only Shell native pin order, including packaged apps and Unicode shortcuts.
//! COM ABI reference: AppSwitcherBar (MIT, Radek Adamec) and Chromium taskbar_util.cc.
//! Only EnumObjects is exposed; this module cannot pin/unpin or modify Explorer.
use std::{ffi::c_void, ptr::null_mut};
use windows::{
    core::{GUID, HRESULT, Interface},
    Win32::{Foundation::S_FALSE, UI::Shell::{
        Common::ITEMIDLIST, IEnumFullIDList, SHGetNameFromIDList,
        SIGDN, SIGDN_DESKTOPABSOLUTEPARSING, SIGDN_NORMALDISPLAY,
    }},
};
use crate::{error::Result, windows_api::Com};

const TASKBAND_PIN: GUID = GUID::from_u128(0x90aa3a4e_1cba_4233_b8bb_535773d48449);
windows_core::imp::define_interface!(ReadPinnedList, ReadPinnedListVtbl, 0x0dd79ae2_d156_45d4_9eeb_3b549769e940);
windows_core::imp::interface_hierarchy!(ReadPinnedList, windows_core::IUnknown);
#[repr(C)]
pub struct ReadPinnedListVtbl {
    pub base__: windows_core::IUnknown_Vtbl,
    pub enum_objects: unsafe extern "system" fn(*mut c_void, *mut *mut c_void) -> HRESULT,
}

struct TaskMem(*mut c_void);
impl Drop for TaskMem {
    fn drop(&mut self) { if !self.0.is_null() { Com::task_mem_free(self.0); } }
}

unsafe fn name(pidl: *const ITEMIDLIST, kind: SIGDN) -> Result<String> {
    let value = unsafe { SHGetNameFromIDList(pidl, kind)? };
    let _allocated = TaskMem(value.0.cast());
    Ok(unsafe { value.to_string()? })
}

pub fn read() -> Result<Vec<(String, String)>> {
    // The caller can be on a Tokio MTA: all Shell objects stay on this bounded STA.
    std::thread::spawn(|| Com::run_with_context(|| unsafe {
        let pins: ReadPinnedList = Com::create_instance(&TASKBAND_PIN)?;
        let mut raw = null_mut();
        (pins.vtable().enum_objects)(pins.as_raw(), &mut raw).ok()?;
        if raw.is_null() { return Err("Shell returned no native pin enumerator".into()); }
        let items = IEnumFullIDList::from_raw(raw);
        let mut result = Vec::new();
        for _ in 0..4096 {
            let mut ptrs = [null_mut()];
            let mut fetched = 0;
            let hr = items.Next(&mut ptrs, Some(&mut fetched));
            let _pidl = TaskMem(ptrs[0].cast());
            if hr == S_FALSE && fetched == 0 { return Ok(result); }
            hr.ok()?;
            if fetched != 1 || ptrs[0].is_null() { return Err("Incomplete native pin enumeration".into()); }
            result.push((name(ptrs[0], SIGDN_NORMALDISPLAY)?, name(ptrs[0], SIGDN_DESKTOPABSOLUTEPARSING)?));
        }
        Err("Native pin enumeration exceeded safety limit".into())
    })).join().map_err(|_| "Native pin reader failed")?
}
