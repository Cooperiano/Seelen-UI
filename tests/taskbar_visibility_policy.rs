#[path="../src/background/modules/desktop_suite/taskbar_policy.rs"] mod policy;

#[test] fn display_mode_is_the_only_replacement_intent() {
    for mode in ["Never", "Always", "OnOverlap"] {
        assert!(policy::configured(true, mode));
        assert!(!policy::configured(false, mode));
    }
    for mode in ["", "unexpected", "never"] { assert!(!policy::configured(true, mode)); }
}

#[test] fn intentional_hiding_is_not_permission_to_ignore_failure() {
    for age in [0, 4999, 5000, 8000] {
        for alive in [false, true] { for healthy in [false, true] {
            assert_eq!(policy::may_hide(true, alive, healthy, age), alive && healthy && age < 5000);
        }}
    }
}

#[test] fn recovery_preserves_every_original_native_state() {
    for original in 0..=3 {
        assert!(policy::should_restore_bar(original, Some(false)));
        assert!(policy::should_restore_bar(original, Some(true)));
        assert!(policy::should_restore_bar(original, None));
    }
}
