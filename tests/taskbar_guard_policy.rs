#[path="../src/background/modules/desktop_suite/taskbar_policy.rs"] mod policy;
#[test] fn hide_requires_every_health_gate() {
 for enabled in [false,true] { for alive in [false,true] { for ready in [false,true] {
  for age in [0,4999,5000,6000] {
   assert_eq!(policy::may_hide(enabled,alive,ready,age),enabled&&alive&&ready&&age<5000);
  }
 } } }
}
