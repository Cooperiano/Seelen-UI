//! Pure one-time native pin ordering policy, independent of Shell/COM and disk.
pub fn plan_order(native: usize, existing: usize, matches: impl Fn(usize, usize) -> bool) -> Result<Vec<Option<usize>>, &'static str> {
    let mut consumed = std::collections::HashSet::new();
    (0..native).map(|n| {
        let candidates: Vec<_> = (0..existing).filter(|e| matches(n, *e)).collect();
        if candidates.len() > 1 { return Err("Ambiguous existing native pin identity"); }
        if let Some(e) = candidates.first() {
            if !consumed.insert(*e) { return Err("Two native pins share an existing identity"); }
        }
        Ok(candidates.first().copied())
    }).collect()
}
/// Pending receipts also block replay: interruption must never overwrite later edits.
pub fn may_migrate(receipt_exists: bool) -> bool { !receipt_exists }
#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn imports_every_pin_in_shell_order_and_retains_existing_ids() {
        let native = ["explorer", "terminal!App", "settings!App", "codex!App", "claude!App", "微信"];
        let existing = ["微信", "extra", "explorer", "codex!App"];
        let plan = plan_order(native.len(), existing.len(), |n,e| native[n] == existing[e]).unwrap();
        assert_eq!(plan, vec![Some(2), None, None, Some(3), None, Some(0)]);
    }

    #[test]
    fn rejects_ambiguous_existing_identity_instead_of_dropping_a_pin() {
        assert!(plan_order(1, 2, |_,_| true).is_err());
    }

    #[test]
    fn rejects_two_native_pins_resolving_to_same_existing_item() {
        assert!(plan_order(2, 1, |_,_| true).is_err());
    }

    #[test]
    fn empty_and_completely_new_inventories_are_valid() {
        assert_eq!(plan_order(0, 9, |_,_| false).unwrap(), Vec::<Option<usize>>::new());
        assert_eq!(plan_order(14, 0, |_,_| false).unwrap(), vec![None; 14]);
    }

    #[test]
    fn completed_or_pending_receipt_never_rewrites_manual_order() {
        assert!(!may_migrate(true));
        assert!(may_migrate(false));
    }
}
