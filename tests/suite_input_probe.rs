//! Opt-in local diagnostic: only prints the adapter's mode label, never text/keys/titles.
#[path = "../src/background/modules/desktop_suite/contract.rs"]
mod contract;
#[path = "../src/background/modules/desktop_suite/input.rs"]
mod input;
fn main() {
    let watch = std::env::args().any(|arg| arg == "--watch");
    let mut previous = String::new();
    for _ in 0..if watch { 400 } else { 1 } {
        let mode = input::indicator();
        if !watch || mode != previous { println!("mode={mode}"); previous = mode; }
        if watch { std::thread::sleep(std::time::Duration::from_millis(100)); }
    }
}
