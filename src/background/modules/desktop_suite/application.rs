//! Stats is the only publisher. This module only reads its existing v7 mapping.
use super::contract::{self, FRAME_SIZE, Snapshot};
use std::{
    sync::{
        LazyLock, Mutex,
        atomic::{Ordering, fence},
    },
    time::{Duration, Instant},
};
use windows::{
    Win32::{
        Foundation::{CloseHandle, HANDLE},
        System::Memory::{
            FILE_MAP_READ, MEMORY_MAPPED_VIEW_ADDRESS, MapViewOfFile, OpenFileMappingW,
            UnmapViewOfFile,
        },
    },
    core::w,
};

pub(super) struct Mapping {
    pub handle: HANDLE,
    pub view: MEMORY_MAPPED_VIEW_ADDRESS,
}
// SAFETY: persistent mappings are only accessed through the enclosing Mutex.
unsafe impl Send for Mapping {}
impl Drop for Mapping {
    fn drop(&mut self) {
        unsafe {
            let _ = UnmapViewOfFile(self.view);
            let _ = CloseHandle(self.handle);
        }
    }
}
static FRESHNESS: LazyLock<Mutex<Option<(i64, Instant)>>> = LazyLock::new(|| Mutex::new(None));

pub fn metrics() -> Option<Snapshot> {
    // SAFETY: exact-size read-only view; independent sequence reads surround the volatile copy.
    let snapshot = unsafe {
        let handle =
            OpenFileMappingW(FILE_MAP_READ.0, false, w!("Local\\Monitoring.Metrics.v7")).ok()?;
        let view = MapViewOfFile(handle, FILE_MAP_READ, 0, 0, FRAME_SIZE);
        if view.Value.is_null() {
            let _ = CloseHandle(handle);
            return None;
        }
        let mapping = Mapping { handle, view };
        let base = mapping.view.Value.cast::<u8>();
        let sequence = base.add(8).cast::<i64>();
        let mut snapshot = None;
        for _ in 0..3 {
            let before = sequence.read_volatile();
            if before <= 0 || before & 1 != 0 {
                std::hint::spin_loop();
                continue;
            }
            fence(Ordering::SeqCst);
            let bytes = base.cast::<[u8; FRAME_SIZE]>().read_volatile();
            fence(Ordering::SeqCst);
            let after = sequence.read_volatile();
            if let Some(value) = contract::decode(&bytes, before, after) {
                snapshot = Some(value);
                break;
            }
        }
        snapshot?
    };
    let mut last = FRESHNESS.lock().ok()?;
    match *last {
        Some((sequence, time)) if sequence == snapshot.sequence => {
            if time.elapsed() > Duration::from_secs(5) {
                return None;
            }
        }
        _ => *last = Some((snapshot.sequence, Instant::now())),
    }
    Some(snapshot)
}
