//! Read-only consumer of Monitoring.Contracts/native/monitoring_contracts.h v7.
//! No change to the publisher or its 272-byte ABI. `timestamp` is a seqlock, not time.
pub const METRIC_COUNT: usize = 14;
pub const FRAME_SIZE: usize = 272;
pub const MODULES: [&str; METRIC_COUNT] = [
    "cpu",
    "gpu",
    "ram",
    "power",
    "cpu_temp",
    "cpu_freq",
    "gpu_temp",
    "gpu_power",
    "gpu_memory",
    "disk",
    "network_down",
    "network_up",
    "battery",
    "bluetooth",
];

pub struct Snapshot {
    pub sequence: i64,
    pub enabled_mask: u32,
    pub values: [Option<f64>; METRIC_COUNT],
    pub order: Vec<u32>,
    pub styles: [u8; METRIC_COUNT],
    pub colors: [u32; METRIC_COUNT],
}

pub fn decode(bytes: &[u8], before: i64, after: i64) -> Option<Snapshot> {
    if bytes.len() != FRAME_SIZE || before <= 0 || before & 1 != 0 || before != after {
        return None;
    }
    let u32_at = |at| u32::from_le_bytes(bytes[at..at + 4].try_into().expect("bounded ABI offset"));
    let sequence = i64::from_le_bytes(bytes[8..16].try_into().ok()?);
    if u32_at(0) != 0x3154424d || u32_at(4) != 7 || sequence != after {
        return None;
    }
    let offsets = [16, 24, 32, 40, 48, 56, 64, 72, 80, 104, 112, 120, 96, 128];
    let values = std::array::from_fn(|id| {
        let at = offsets[id];
        let value = f64::from_le_bytes(bytes[at..at + 8].try_into().expect("bounded ABI offset"));
        let valid = match id {
            0 | 1 | 2 | 9 | 12 => (0.0..=100.0).contains(&value),
            3 | 4 | 6 => (-20.0..200.0).contains(&value),
            5 => value > 0.0 && value < 10000.0,
            7 => (0.0..10000.0).contains(&value),
            13 => (0.0..1000.0).contains(&value),
            _ => value >= 0.0,
        };
        (value.is_finite() && valid).then_some(value)
    });
    let enabled_mask = u32_at(136) & ((1 << METRIC_COUNT) - 1);
    let mut order = Vec::new();
    for index in 0..u32_at(140).min(METRIC_COUNT as u32) as usize {
        let id = u32_at(144 + index * 4);
        if id < METRIC_COUNT as u32 && enabled_mask & (1 << id) != 0 && !order.contains(&id) {
            order.push(id);
        }
    }
    for id in 0..METRIC_COUNT as u32 {
        if enabled_mask & (1 << id) != 0 && !order.contains(&id) {
            order.push(id);
        }
    }
    Some(Snapshot {
        sequence,
        enabled_mask,
        values,
        order,
        styles: std::array::from_fn(|id| bytes[200 + id]),
        colors: std::array::from_fn(|id| u32_at(216 + id * 4)),
    })
}

/// Accepted Brimbar .22 policy: two identical getter samples and a stable target.
/// `None` is unknown, never an invented English state.
pub fn stable_mode(
    target_current: bool,
    first: Option<(usize, usize)>,
    second: Option<(usize, usize)>,
) -> Option<bool> {
    let first = first?;
    if !target_current || Some(first) != second || first.0 > 1 || first.1 > 0xffff {
        return None;
    }
    Some(first.0 != 0 && first.1 & 1 != 0)
}
