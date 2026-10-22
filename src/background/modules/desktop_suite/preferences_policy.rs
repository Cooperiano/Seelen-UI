//! Bounded component settings edits. Legacy tracking values and unknown fields
//! remain owned by their existing component; navigation never normalizes them.
use serde_json::Value;
use std::collections::BTreeSet;

const METRICS: &[&str] = &["cpu", "gpu", "ram", "power", "cpu_temp", "cpu_freq", "gpu_temp",
    "gpu_power", "gpu_memory", "disk", "network_down", "network_up", "battery", "bluetooth"];

pub fn apply(module: &str, original: &Value, patch: &Value) -> Result<Value, String> {
    let source = original.as_object().ok_or("Settings document is not an object")?;
    let edits = patch.as_object().ok_or("Settings patch is not an object")?;
    if module == "touchpad" && source.get("SettingsVersion").and_then(Value::as_u64) != Some(9) {
        return Err("Unsupported touchpad settings version; original preserved".into());
    }
    if module != "stats" && module != "touchpad" { return Err("Unknown component".into()); }
    let mut next = original.clone();
    for (key, value) in edits {
        let valid = match (module, key.as_str()) {
            ("stats", "LowPowerMode") | ("touchpad", "ThreeFingerDrag" | "FourFingerGestures" | "RecordLogs") => value.is_boolean(),
            ("stats", "RefreshIntervalMilliseconds") => integer(value, 500, 5000),
            ("stats", "HistorySeconds") => integer(value, 30, 300),
            ("stats", "Metrics") => validate_metrics(&original["Metrics"], value),
            ("stats", "Alerts") => validate_alerts(&original["Alerts"], value),
            ("touchpad", "ThreeFingerDragButton") => integer(value, 0, 3),
            ("touchpad", "ThreeFingerDragStartThreshold") => integer(value, 0, 1000),
            ("touchpad", "FourFingerSwipeThreshold") => integer(value, 40, 1000),
            ("touchpad", "Language") => matches!(value.as_str(), Some("system" | "en" | "zh-Hans")),
            _ => false,
        };
        if !valid { return Err(format!("Invalid or protected setting: {key}")); }
        next[key] = value.clone();
    }
    if module == "touchpad" && edits.contains_key("ThreeFingerDragStartThreshold") {
        let start = next["ThreeFingerDragStartThreshold"].as_u64().ok_or("Invalid start threshold")?;
        let stop = next["ThreeFingerDragStopThreshold"].as_u64().ok_or("Invalid stop threshold")?;
        next["ThreeFingerDragStopThreshold"] = Value::from(stop.min(start));
    }
    Ok(next)
}

fn integer(value: &Value, min: u64, max: u64) -> bool {
    value.as_u64().is_some_and(|v| (min..=max).contains(&v))
}
fn same_ids(original: &Value, changed: &Value, key: &str) -> bool {
    let Some(before) = original.as_array() else { return false; };
    let Some(after) = changed.as_array() else { return false; };
    let before_ids: BTreeSet<_> = before.iter().filter_map(|v| v[key].as_str()).collect();
    let after_ids: BTreeSet<_> = after.iter().filter_map(|v| v[key].as_str()).collect();
    before.len() == before_ids.len() && after.len() == after_ids.len() && before_ids == after_ids
}
fn validate_metrics(original: &Value, changed: &Value) -> bool {
    same_ids(original, changed, "Id") && changed.as_array().is_some_and(|rows| rows.iter().all(|row| {
        row["Id"].as_str().is_some_and(|id| METRICS.contains(&id)) && row["IsEnabled"].is_boolean()
            && matches!(row["DisplayStyle"].as_str(), Some("stacked" | "inline" | "value" | "chart"))
            && row["Accent"].as_str().is_some_and(|color| color.len() == 9 && color.starts_with('#')
                && color.as_bytes()[1..].iter().all(u8::is_ascii_hexdigit))
    }))
}
fn validate_alerts(original: &Value, changed: &Value) -> bool {
    same_ids(original, changed, "MetricId") && changed.as_array().is_some_and(|rows| rows.iter().all(|row| {
        row["MetricId"].as_str().is_some_and(|id| METRICS.contains(&id)) && row["IsEnabled"].is_boolean()
            && matches!(row["Comparison"].as_str(), Some("above" | "below"))
            && integer(&row["SustainSeconds"], 0, 300)
            && row["Threshold"].as_f64().is_some_and(|v| v.is_finite() && v >= 0.0
                && v <= if row["MetricId"] == "cpu_freq" { 10000.0 } else { 200.0 })
    }))
}
