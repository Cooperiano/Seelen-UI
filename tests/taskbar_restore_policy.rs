#[path="../src/background/modules/desktop_suite/taskbar_policy.rs"] mod policy;
#[test]
fn an_always_visible_original_shell_must_not_inherit_a_transient_hidden_snapshot(){
    assert!(policy::should_restore_bar(0,Some(false)));
    assert!(policy::should_restore_bar(2,Some(false)));
}
#[test]
fn preserve_real_autohide_and_restore_newly_attached_monitors(){
    for state in 0..=3 {
        assert!(policy::should_restore_bar(state,Some(true)));
        assert!(policy::should_restore_bar(state,None));
    }
    // SW_HIDE is not Explorer's automatic edge-collapse state. Even with the
    // auto-hide flag set, a hidden visibility snapshot must not leave it disabled.
    assert!(policy::should_restore_bar(1,Some(false)));
    assert!(policy::should_restore_bar(3,Some(false)));
}
#[test]
fn malformed_shell_flags_are_not_restoration_authority(){
    for state in [4, 64, u32::MAX] {
        for visibility in [Some(false), Some(true), None] {
            assert!(!policy::should_restore_bar(state,visibility));
        }
    }
}
