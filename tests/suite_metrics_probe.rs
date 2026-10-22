//! Read-only live fixture using the exact native adapter linked into the toolbar.
#[path = "../src/background/modules/desktop_suite/contract.rs"]
mod contract;
#[path = "../src/background/modules/desktop_suite/application.rs"]
mod application;
fn main() {
    for _ in 0..2 {
        match application::metrics() {
            Some(s) => println!("sequence={} mask={} order={:?} values={:?}", s.sequence, s.enabled_mask, s.order, s.values),
            None => { eprintln!("Stats snapshot unavailable"); std::process::exit(1); }
        }
        std::thread::sleep(std::time::Duration::from_secs(2));
    }
}
