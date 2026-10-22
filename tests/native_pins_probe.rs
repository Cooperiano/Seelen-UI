//! Local read-only ABI probe using the production Shell reader, no GUI input.
mod error { pub type Result<T> = std::result::Result<T, Box<dyn std::error::Error + Send + Sync>>; }
mod windows_api {
    use windows::{core::{GUID,Interface}, Win32::System::Com::{CoInitializeEx,CoUninitialize,COINIT_APARTMENTTHREADED,CoCreateInstance,CLSCTX_ALL,CoTaskMemFree}};
    pub struct Com;
    impl Com {
        pub fn run_with_context<T>(f:impl FnOnce()->crate::error::Result<T>)->crate::error::Result<T>{
            unsafe{CoInitializeEx(None,COINIT_APARTMENTTHREADED).ok()?;}
            struct Guard;impl Drop for Guard{fn drop(&mut self){unsafe{CoUninitialize()}}}
            let _guard=Guard;f()
        }
        pub fn create_instance<T:Interface>(id:&GUID)->crate::error::Result<T>{Ok(unsafe{CoCreateInstance(id,None,CLSCTX_ALL)?})}
        pub fn task_mem_free(ptr:*mut std::ffi::c_void){unsafe{CoTaskMemFree(Some(ptr))}}
    }
}
#[path="../src/background/widgets/weg/native_pins.rs"] mod native_pins;
fn main()->error::Result<()> {
    let pins=native_pins::read()?;
    println!("pins={}",pins.len());
    for (name,path) in pins {println!("{name}\t{path}");}
    Ok(())
}
