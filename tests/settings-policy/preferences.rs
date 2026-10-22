#[path = "../../src/background/modules/desktop_suite/preferences_policy.rs"]
mod policy;
use serde_json::{Value, json};
use policy as preferences_policy;
#[path = "../../src/background/modules/desktop_suite/preferences_store.rs"]
mod store;
use std::{path::{Path,PathBuf}, sync::atomic::{AtomicU64,Ordering}};

struct Fixture(PathBuf);
impl Fixture {
    fn new() -> Self {
        static NEXT: AtomicU64=AtomicU64::new(0);
        let path=std::env::temp_dir().join(format!("suite-settings-test-{}-{}",std::process::id(),NEXT.fetch_add(1,Ordering::Relaxed)));
        std::fs::create_dir(&path).unwrap(); Self(path)
    }
    fn path(&self)->PathBuf{self.0.join("settings.json")}
}
impl Drop for Fixture {fn drop(&mut self){let _=std::fs::remove_dir_all(&self.0);}}
#[derive(Default)] struct Runtime {stops:u32,starts:u32,fail_start:bool}
impl store::Runtime for Runtime {
    fn stop(&mut self)->Result<(),String>{self.stops+=1;Ok(())}
    fn resume(&mut self)->Result<(),String>{self.starts+=1;if self.fail_start && self.starts==1 {Err("restart failed".into())} else {Ok(())}}
}
fn write(path:&Path,bytes:&[u8])->Result<(),String>{std::fs::write(path,bytes).map_err(|e|e.to_string())}

#[test] fn stale_revision_does_not_stop_or_write() {
    let temp=Fixture::new();std::fs::write(temp.path(),stats().to_string()).unwrap();
    let mut runtime=Runtime::default();
    assert!(store::commit(&temp.path(),"stats","stale",&json!({"LowPowerMode":true}),&mut runtime,write).is_err());
    assert_eq!(runtime.stops,0);assert_eq!(store::read(&temp.path()).unwrap().data,stats());
}
#[test] fn unchanged_save_never_restarts_or_reformats_original_bytes() {
    let temp=Fixture::new();let bytes=serde_json::to_vec_pretty(&touchpad()).unwrap();std::fs::write(temp.path(),&bytes).unwrap();
    let before=store::read(&temp.path()).unwrap();let mut runtime=Runtime::default();
    store::commit(&temp.path(),"touchpad",&before.revision,&json!({"ThreeFingerDrag":true}),&mut runtime,write).unwrap();
    assert_eq!((runtime.stops,runtime.starts),(0,0));assert_eq!(std::fs::read(temp.path()).unwrap(),bytes);
}
#[test] fn changed_commit_backs_up_exact_bytes_and_restores_runtime() {
    let temp=Fixture::new();let bytes=touchpad().to_string();std::fs::write(temp.path(),&bytes).unwrap();
    let before=store::read(&temp.path()).unwrap();let mut runtime=Runtime::default();
    let after=store::commit(&temp.path(),"touchpad",&before.revision,&json!({"ThreeFingerDrag":false}),&mut runtime,write).unwrap();
    assert_eq!(after.data["ThreeFingerDrag"],false);assert_eq!((runtime.stops,runtime.starts),(1,1));
    assert_eq!(std::fs::read_to_string(temp.path().with_extension(format!("suite-{}.bak",before.revision))).unwrap(),bytes);
}
#[test] fn failed_runtime_recovery_restores_original_bytes_and_reports_failure() {
    let temp=Fixture::new();let bytes=touchpad().to_string();std::fs::write(temp.path(),&bytes).unwrap();
    let before=store::read(&temp.path()).unwrap();let mut runtime=Runtime{fail_start:true,..Default::default()};
    assert!(store::commit(&temp.path(),"touchpad",&before.revision,&json!({"ThreeFingerDrag":false}),&mut runtime,write).is_err());
    assert_eq!(std::fs::read_to_string(temp.path()).unwrap(),bytes);assert_eq!(runtime.starts,2);
}
#[test] fn failed_write_preserves_bytes_and_recovers_stopped_component() {
    let temp=Fixture::new();let bytes=touchpad().to_string();std::fs::write(temp.path(),&bytes).unwrap();
    let before=store::read(&temp.path()).unwrap();let mut runtime=Runtime::default();
    assert!(store::commit(&temp.path(),"touchpad",&before.revision,&json!({"ThreeFingerDrag":false}),&mut runtime,|_,_|Err("disk error".into())).is_err());
    assert_eq!(std::fs::read_to_string(temp.path()).unwrap(),bytes);assert_eq!(runtime.starts,1);
}
#[test] fn invalid_documents_and_large_input_fail_without_creating_defaults() {
    let temp=Fixture::new();assert!(store::read(&temp.path()).is_err());assert!(!temp.path().exists());
    std::fs::write(temp.path(),"not json").unwrap();assert!(store::read(&temp.path()).is_err());
    std::fs::write(temp.path(),vec![b' ';131073]).unwrap();assert!(store::read(&temp.path()).is_err());
}

fn stats() -> Value {
    json!({"Metrics":[{"Id":"cpu","IsEnabled":true,"DisplayStyle":"stacked","Accent":"#FF65D5FF"}],
        "RefreshIntervalMilliseconds":1000,"HistorySeconds":60,"LowPowerMode":false,
        "Alerts":[],"FutureField":{"retain":true}})
}
fn touchpad() -> Value {
    json!({"SettingsVersion":9,"ThreeFingerDrag":true,"ThreeFingerDragButton":1,
        "ThreeFingerDragStartThreshold":32,"ThreeFingerDragStopThreshold":10,"FourFingerGestures":false,
        "FourFingerSwipeThreshold":90,"Language":"system","RecordLogs":false,
        "RunElevated":false,"PointerBackupSpeed":10,
        "ThreeFingerDeviceDragCursorConfigs":{"device":{"ThreeFingerDragCursorSpeed":182}},
        "FutureField":{"retain":true}})
}

#[test] fn stats_patch_preserves_unknown_fields_and_unmodified_choices() {
    let value=policy::apply("stats", &stats(), &json!({"LowPowerMode":true})).unwrap();
    assert_eq!(value["Metrics"],stats()["Metrics"]);
    assert_eq!(value["FutureField"],stats()["FutureField"]);
    assert_eq!(value["LowPowerMode"],true);
}
#[test] fn stats_rejects_bad_bounds_types_styles_and_colors() {
    for patch in [json!({"HistorySeconds":29}),json!({"HistorySeconds":301}),json!({"RefreshIntervalMilliseconds":499}),
        json!({"RefreshIntervalMilliseconds":5001}),json!({"LowPowerMode":1}),json!({"HistorySeconds":60.5}),
        json!({"Metrics":[{"Id":"cpu","IsEnabled":true,"DisplayStyle":"bad","Accent":"#FF65D5FF"}]}),
        json!({"Metrics":[{"Id":"cpu","IsEnabled":true,"DisplayStyle":"stacked","Accent":"red"}]})] {
        assert!(policy::apply("stats",&stats(),&patch).is_err(),"{patch}");
    }
}
#[test] fn metrics_reordering_cannot_remove_or_duplicate_existing_ids() {
    let mut original=stats();
    original["Metrics"].as_array_mut().unwrap().push(json!({"Id":"gpu","IsEnabled":false,"DisplayStyle":"stacked","Accent":"#FF72E6AE"}));
    let mut reordered=original["Metrics"].as_array().unwrap().clone(); reordered.reverse();
    assert_eq!(policy::apply("stats",&original,&json!({"Metrics":reordered})).unwrap()["Metrics"][0]["Id"],"gpu");
    assert!(policy::apply("stats",&original,&json!({"Metrics":[reordered[0].clone()]})).is_err());
    assert!(policy::apply("stats",&original,&json!({"Metrics":[reordered[0].clone(),reordered[0].clone()]})).is_err());
}
#[test] fn alert_validation_uses_catalog_limits_and_preserves_rule_identity() {
    let mut original=stats();original["Alerts"]=json!([{"MetricId":"cpu","IsEnabled":false,"Threshold":90,"Comparison":"above","SustainSeconds":10}]);
    let good=json!({"Alerts":[{"MetricId":"cpu","IsEnabled":true,"Threshold":85,"Comparison":"above","SustainSeconds":10}]});
    assert!(policy::apply("stats",&original,&good).is_ok());
    for field in [json!({"Threshold":-1}),json!({"Threshold":201}),json!({"Comparison":"garbage"}),json!({"SustainSeconds":301})] {
        let mut bad=good.clone();for (key,value) in field.as_object().unwrap(){bad["Alerts"][0][key]=value.clone();}
        assert!(policy::apply("stats",&original,&bad).is_err());
    }
}
#[test] fn touchpad_patch_preserves_tracking_backup_device_settings_and_four_finger_choice() {
    let value=policy::apply("touchpad",&touchpad(),&json!({"ThreeFingerDrag":false})).unwrap();
    for key in ["PointerBackupSpeed","ThreeFingerDeviceDragCursorConfigs","FourFingerGestures","FutureField"] {
        assert_eq!(value[key],touchpad()[key]);
    }
}
#[test] fn drag_threshold_also_keeps_stop_threshold_bounded() {
    let value=policy::apply("touchpad",&touchpad(),&json!({"ThreeFingerDragStartThreshold":5})).unwrap();
    assert_eq!(value["ThreeFingerDragStopThreshold"],5);
}
#[test] fn touchpad_rejects_unknown_privileged_legacy_and_out_of_range_fields() {
    for patch in [json!({"ThreeFingerDragButton":4}),json!({"ThreeFingerDragStartThreshold":-1}),json!({"FourFingerSwipeThreshold":39}),
        json!({"Language":"unknown"}),json!({"RunElevated":true}),json!({"StartupAction":1}),
        json!({"ThreeFingerDeviceDragCursorConfigs":{}}),json!({"ThreeFingerDrag":1})] {
        assert!(policy::apply("touchpad",&touchpad(),&patch).is_err(),"{patch}");
    }
}
#[test] fn protected_or_future_documents_are_never_overwritten() {
    let mut future=touchpad();future["SettingsVersion"]=json!(10);
    for original in [future,Value::Null,json!([])]{assert!(policy::apply("touchpad",&original,&json!({"RecordLogs":true})).is_err());}
    assert!(policy::apply("unknown",&stats(),&json!({"LowPowerMode":true})).is_err());
}
#[test] fn no_op_patch_preserves_value_exactly() {
    assert_eq!(policy::apply("stats",&stats(),&json!({})).unwrap(),stats());
    assert_eq!(policy::apply("touchpad",&touchpad(),&json!({"ThreeFingerDrag":true})).unwrap(),touchpad());
}
