#[path = "../src/background/modules/desktop_suite/contract.rs"]
mod contract;
use contract::*;

#[test]
fn compact_metric_theme_defines_child_line_boxes_and_removes_default_button_gap() {
    let css = include_str!("../preview-resources/suite-fit/toolbar.scss");
    assert!(css.contains("gap: 0"));
    assert!(css.contains(".suite-metric-label") && css.contains("line-height: 1"));
    assert!(css.contains(".suite-metric-value"));
}

#[test]
fn opening_control_center_never_configures_hotspot_credentials() {
    let source = include_str!("../src/background/modules/network/application.rs");
    let getter = source
        .split("pub fn get_hotspot()")
        .nth(1)
        .unwrap()
        .split("pub fn ")
        .next()
        .unwrap();
    for setter in ["ConfigureAccessPointAsync", "SetPassphrase", "SetSsid"] {
        assert!(
            !getter.contains(setter),
            "Read-only hotspot getter contains security-setting write: {setter}"
        );
    }
}

#[test]
fn preview_background_capture_and_hotspot_secret_guards_remain_explicit() {
    let startup: String = include_str!("../src/background/app.rs")
        .chars()
        .filter(|c| !c.is_whitespace())
        .collect();
    assert!(startup.contains(
        "if!slu_ipc::identity::SUITE_BUILD{std::thread::spawn(init_clipboard_manager);}"
    ));
    let network: String = include_str!("../src/background/modules/network/application.rs")
        .chars()
        .filter(|c| !c.is_whitespace())
        .collect();
    assert!(network.contains("passphrase:ifslu_ipc::identity::SUITE_BUILD{None}"));
}

fn frame() -> [u8; 272] {
    let mut b = [0u8; 272];
    b[0..4].copy_from_slice(&0x3154424du32.to_le_bytes());
    b[4..8].copy_from_slice(&7u32.to_le_bytes());
    b[8..16].copy_from_slice(&2i64.to_le_bytes());
    b[16..24].copy_from_slice(&42.5f64.to_le_bytes());
    b[136..140].copy_from_slice(&5u32.to_le_bytes());
    b[140..144].copy_from_slice(&3u32.to_le_bytes());
    for (index, id) in [2u32, 0, 2].iter().enumerate() {
        b[144 + index * 4..148 + index * 4].copy_from_slice(&id.to_le_bytes());
    }
    b
}

#[test]
fn decodes_existing_v7_offsets_without_a_second_collector() {
    let s = decode(&frame(), 2, 2).unwrap();
    assert_eq!(s.values[0], Some(42.5));
    assert_eq!(s.order, [2, 0]);
    assert_eq!(s.enabled_mask, 5);
}

#[test]
fn rejects_torn_uncommitted_wrong_version_and_truncated_frames() {
    for (before, after) in [(0, 0), (1, 1), (2, 4), (4, 4)] {
        assert!(decode(&frame(), before, after).is_none());
    }
    let mut b = frame();
    b[4] = 6;
    assert!(decode(&b, 2, 2).is_none());
    b[4] = 7;
    b[0] = 0;
    assert!(decode(&b, 2, 2).is_none());
    assert!(decode(&frame()[..271], 2, 2).is_none());
}

#[test]
fn invalid_hardware_values_remain_unavailable_not_zero() {
    let mut b = frame();
    for value in [f64::NAN, f64::INFINITY, -1.0, 101.0] {
        b[16..24].copy_from_slice(&value.to_le_bytes());
        assert_eq!(decode(&b, 2, 2).unwrap().values[0], None);
    }
    b[40..48].copy_from_slice(&39.0f64.to_le_bytes());
    b[56..64].copy_from_slice(&3500.0f64.to_le_bytes());
    b[96..104].copy_from_slice(&73.0f64.to_le_bytes());
    b[104..112].copy_from_slice(&51.0f64.to_le_bytes());
    let s = decode(&b, 2, 2).unwrap();
    assert_eq!(s.values[3], Some(39.0));
    assert_eq!(s.values[5], Some(3500.0));
    assert_eq!(s.values[12], Some(73.0));
    assert_eq!(s.values[9], Some(51.0));
}

#[test]
fn order_is_bounded_deduplicated_and_keeps_enabled_missing_ids() {
    let mut b = frame();
    b[140..144].copy_from_slice(&u32::MAX.to_le_bytes());
    b[144..148].copy_from_slice(&999u32.to_le_bytes());
    let s = decode(&b, 2, 2).unwrap();
    assert_eq!(s.order, [0, 2]);
}

#[test]
fn pinyin_preserves_shift_mode_and_never_guesses_on_torn_reads() {
    assert_eq!(stable_mode(true, Some((1, 1)), Some((1, 1))), Some(true));
    assert_eq!(stable_mode(true, Some((1, 0)), Some((1, 0))), Some(false));
    assert_eq!(stable_mode(true, Some((0, 1)), Some((0, 1))), Some(false));
    for (target, first, second) in [
        (false, Some((1, 1)), Some((1, 1))),
        (true, None, Some((1, 1))),
        (true, Some((1, 1)), None),
        (true, Some((1, 1)), Some((1, 0))),
        (true, Some((2, 1)), Some((2, 1))),
        (true, Some((1, 65536)), Some((1, 65536))),
    ] {
        assert_eq!(stable_mode(target, first, second), None);
    }
}
