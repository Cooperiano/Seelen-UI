//! Bounded local recovery for the suite's known original shell flags (0).
//! No hooks, injection, Explorer restart, input synthesis or user app actions.
use windows::{core::w,Win32::{Foundation::LPARAM,UI::{Shell::{APPBARDATA,SHAppBarMessage,ABM_SETSTATE},WindowsAndMessaging::{FindWindowW,ShowWindow,SW_SHOWNOACTIVATE,SW_HIDE}}}};
fn main()->windows::core::Result<()> {
    unsafe {
        let hwnd=FindWindowW(w!("Shell_TrayWnd"),None)?;
        let mut data=APPBARDATA{cbSize:std::mem::size_of::<APPBARDATA>() as u32,lParam:LPARAM(0),..Default::default()};
        SHAppBarMessage(ABM_SETSTATE,&mut data);
        let hide=std::env::args().nth(1).as_deref()==Some("--simulate-hidden-before-lease");
        let _=ShowWindow(hwnd,if hide{SW_HIDE}else{SW_SHOWNOACTIVATE});
    }
    Ok(())
}
