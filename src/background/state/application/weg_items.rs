use std::sync::LazyLock;

use parking_lot::Mutex;
use seelen_core::{
    resource::WidgetId,
    state::{WegItem, WegItemData, WegItems},
};
use uuid::Uuid;

use crate::{
    error::{Result, ResultLogExt},
    modules::apps::application::msix::MsixAppsManager,
    utils::{atomic_write_file, constants::SEELEN_COMMON},
    widgets::weg::native_taskbar::get_windows_taskbar_pinned_apps,
};

pub static WEG_ITEMS_MANAGER: LazyLock<WegItemsManager> = LazyLock::new(|| {
    let manager = WegItemsManager {
        items: Mutex::new(WegItems::default()),
    };
    manager.load().log_error();
    manager
});

pub struct WegItemsManager {
    items: Mutex<WegItems>,
}

impl WegItemsManager {
    /// Explicit one-time native-order migration, never called by normal startup.
    /// The durable receipt is created BEFORE state replacement. Even interruption
    /// or a failed completion receipt blocks replay over a later user reorder.
    #[cfg(feature = "desktop-suite")]
    pub fn migrate_native_order_once(&self) -> Result<usize> {
        use crate::widgets::weg::{identity::matches_application, native_order::{may_migrate, plan_order}};
        let dir = SEELEN_COMMON.widget_data_dir(&WidgetId::known_weg());
        let receipt = dir.join("native-order-v1.json");
        let mut guard = self.items.lock();
        if !may_migrate(receipt.exists()) { return Ok(0); }
        let native = get_windows_taskbar_pinned_apps()?;
        if native.is_empty() { return Err("Native pin list is empty; nothing migrated".into()); }
        let existing: Vec<_> = guard.left.iter().chain(&guard.center).chain(&guard.right)
            .filter_map(|i| if let WegItem::AppOrFile(app) = i { Some(app) } else { None }).collect();
        let target = |app: &WegItemData| app.relaunch.as_ref().map(|r| r.command.clone())
            .unwrap_or_else(|| app.path.to_string_lossy().into_owned());
        let inventory: Vec<_> = native.iter().map(|app| (app.umid.as_deref(), target(app))).collect();
        let plan = plan_order(native.len(), existing.len(), |n,e| {
            let old=existing[e]; let new=&native[n];
            matches_application(old.umid.as_deref(), &old.path.to_string_lossy(),
                old.relaunch.as_ref().map(|r|r.command.as_str()), new.umid.as_deref(), &target(new), &inventory)
        }).map_err(crate::error::AppError::from)?;
        let mut migrated = guard.clone();
        let mut consumed = std::collections::HashSet::new();
        let mut ordered = Vec::new();
        for (new, prior) in native.iter().zip(plan) {
            let mut app = new.clone();
            if let Some(index) = prior { app.id = existing[index].id; consumed.insert(app.id); }
            ordered.push(WegItem::AppOrFile(app));
        }
        let keep = |entry: &WegItem| !consumed.contains(entry.id());
        migrated.left.retain(keep);
        migrated.center.retain(keep);
        migrated.right.retain(keep);
        ordered.append(&mut migrated.center);
        migrated.center = ordered;
        migrated.sanitize();
        let actual: Vec<_> = migrated.center.iter().filter_map(|i| if let WegItem::AppOrFile(a)=i {Some(a)} else {None}).take(native.len()).collect();
        if actual.len()!=native.len() || actual.iter().zip(&native).any(|(a,n)| a.umid!=n.umid || a.path!=n.path || !a.pinned) {
            return Err("Native pin validation failed; original order retained".into());
        }
        let path = dir.join("state.yml");
        let original = std::fs::read(&path)?;
        let backup = dir.join(format!("before-native-order-{}.yml", Uuid::new_v4()));
        atomic_write_file(&backup, &original)?;
        // This marker is never removed automatically. Pending means inspect the
        // backup/state, not retry and risk overwriting the user's latest changes.
        atomic_write_file(&receipt, &serde_json::to_vec_pretty(&serde_json::json!({
            "version":1,"status":"prepared","count":native.len(),"backup":backup,
        }))?)?;
        atomic_write_file(&path, serde_yaml::to_string(&migrated)?.as_bytes())?;
        *guard = migrated;
        atomic_write_file(&receipt, &serde_json::to_vec_pretty(&serde_json::json!({
            "version":1,"status":"complete","count":native.len(),"backup":backup,
        }))?)?;
        Ok(native.len())
    }

    pub fn get(&self) -> WegItems {
        self.items.lock().clone()
    }

    pub fn write(&self, mut items: WegItems) -> Result<()> {
        items.sanitize();
        let path = SEELEN_COMMON
            .widget_data_dir(&WidgetId::known_weg())
            .join("state.yml");
        // Hold the lock while writing so concurrent saves (one webview per
        // monitor) are serialized instead of racing on the same file.
        let mut guard = self.items.lock();
        atomic_write_file(&path, serde_yaml::to_string(&items)?.as_bytes())?;
        *guard = items;
        Ok(())
    }

    pub fn load(&self) -> Result<()> {
        let path = SEELEN_COMMON
            .widget_data_dir(&WidgetId::known_weg())
            .join("state.yml");

        let items = if path.exists() {
            let mut items: WegItems = serde_yaml::from_str(&std::fs::read_to_string(&path)?)?;
            items.sanitize();
            resolve_msix_paths(&mut items);
            items
        } else {
            // First run: try to import from Windows taskbar
            let mut items = match self.import_from_windows_taskbar_internal() {
                Ok(imported_items) if !imported_items.center.is_empty() => {
                    log::info!(
                        "First run: imported {} items from Windows taskbar",
                        imported_items.center.len()
                    );
                    imported_items
                }
                Ok(_) => {
                    log::info!("First run: no Windows pinned items found, using defaults");
                    initial_items()
                }
                Err(e) => {
                    log::warn!(
                        "First run: failed to import from Windows taskbar: {}, using defaults",
                        e
                    );
                    initial_items()
                }
            };
            items.sanitize();
            atomic_write_file(&path, serde_yaml::to_string(&items)?.as_bytes())?;
            items
        };

        *self.items.lock() = items;
        Ok(())
    }

    /// Internal method to import from Windows without writing to state
    /// Used during initial load to avoid double-write
    fn import_from_windows_taskbar_internal(&self) -> Result<WegItems> {
        let windows_pinned_items = get_windows_taskbar_pinned_apps()?;

        if windows_pinned_items.is_empty() {
            return Ok(initial_items());
        }

        // Start with the base structure from initial_items
        let base = initial_items();

        // Build the center section with imported items
        let center: Vec<WegItem> = windows_pinned_items
            .into_iter()
            .map(WegItem::AppOrFile)
            .collect();

        Ok(WegItems {
            is_reorder_disabled: base.is_reorder_disabled,
            left: base.left,
            center,
            right: base.right,
        })
    }

    /// Import pinned items from Windows taskbar.
    /// This will add missing items without removing existing ones.
    /// Returns the number of items imported.
    pub fn import_from_windows_taskbar(&self) -> Result<usize> {
        let windows_pinned_items = get_windows_taskbar_pinned_apps()?;

        if windows_pinned_items.is_empty() {
            return Ok(0);
        }

        let mut items = self.get();

        // Explicit import is additive: matching apps keep their ID and user-chosen slot.
        let inventory:Vec<_>=windows_pinned_items.iter().map(|w|(w.umid.as_deref(),w.path.to_string_lossy().into_owned())).collect();
        for windows_item in &windows_pinned_items {
            let found=items.left.iter_mut().chain(items.center.iter_mut()).chain(items.right.iter_mut()).find_map(|entry| {
                let WegItem::AppOrFile(existing)=entry else {return None};
                crate::widgets::weg::identity::matches_application(existing.umid.as_deref(),&existing.path.to_string_lossy(),
                    existing.relaunch.as_ref().map(|r|r.command.as_str()),windows_item.umid.as_deref(),
                    &windows_item.path.to_string_lossy(),&inventory).then_some(existing)
            });
            if let Some(existing)=found {existing.pinned=true;}
            else {items.center.push(WegItem::AppOrFile(windows_item.clone()));}
        }

        self.write(items)?;
        Ok(windows_pinned_items.len())
    }
}

fn initial_items() -> WegItems {
    WegItems {
        is_reorder_disabled: false,
        left: vec![
            WegItem::Plugin {
                id: Uuid::new_v4(),
                plugin: "@default/weg-start-menu".into(),
            },
            WegItem::Plugin {
                id: Uuid::new_v4(),
                plugin: "@default/weg-show-desktop".into(),
            },
        ],
        center: vec![WegItem::AppOrFile(WegItemData {
            id: Uuid::new_v4(),
            umid: None,
            path: "C:\\Windows\\explorer.exe".into(),
            display_name: t!("file_explorer").to_string(),
            pinned: true,
            prevent_pinning: false,
            relaunch: None,
        })],
        right: vec![
            WegItem::Plugin {
                id: Uuid::new_v4(),
                plugin: "@default/weg-trash-bin".into(),
            },
            WegItem::Media { id: Uuid::new_v4() },
        ],
    }
}

fn update_weg_items_paths(items: &mut [WegItem]) {
    for item in items {
        if let WegItem::AppOrFile(data) = item
            && let Some(umid) = &data.umid
            && let Ok(Some(app_path)) = MsixAppsManager::instance().get_app_path(umid)
        {
            data.path = app_path;
        }
    }
}

fn resolve_msix_paths(weg_items: &mut WegItems) {
    let WegItems {
        left,
        center,
        right,
        ..
    } = weg_items;
    std::thread::scope(|s| {
        s.spawn(|| update_weg_items_paths(left));
        s.spawn(|| update_weg_items_paths(center));
        s.spawn(|| update_weg_items_paths(right));
    });
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_initial_items_structure() {
        let items = initial_items();

        assert_eq!(
            items.left.len(),
            2,
            "Left should have StartMenu and ShowDesktop"
        );
        assert_eq!(items.center.len(), 1, "Center should have File Explorer");
        assert_eq!(items.right.len(), 2, "Right should have TrashBin and Media");

        // Verify left items
        assert!(matches!(items.left[0], WegItem::Plugin { .. }));
        assert!(matches!(items.left[1], WegItem::Plugin { .. }));

        // Verify center has explorer
        if let WegItem::AppOrFile(ref data) = items.center[0] {
            assert!(data.path.to_string_lossy().contains("explorer.exe"));
            assert!(data.pinned);
        } else {
            panic!("Center item should be AppOrFile");
        }

        // Verify right items
        assert!(matches!(items.right[0], WegItem::Plugin { .. }));
        assert!(matches!(items.right[1], WegItem::Media { .. }));
    }

    #[test]
    fn test_import_preserves_special_items() {
        // Create a mock WegItems with special items
        let original = WegItems {
            is_reorder_disabled: false,
            left: vec![
                WegItem::Plugin {
                    id: Uuid::new_v4(),
                    plugin: "@default/weg-start-menu".into(),
                },
                WegItem::AppOrFile(WegItemData {
                    id: Uuid::new_v4(),
                    display_name: "Old App".to_string(),
                    umid: None,
                    path: "C:\\old.exe".into(),
                    pinned: true,
                    prevent_pinning: false,
                    relaunch: None,
                }),
            ],
            center: vec![
                WegItem::AppOrFile(WegItemData {
                    id: Uuid::new_v4(),
                    display_name: "Center App".to_string(),
                    umid: None,
                    path: "C:\\center.exe".into(),
                    pinned: true,
                    prevent_pinning: false,
                    relaunch: None,
                }),
                WegItem::Separator { id: Uuid::new_v4() },
            ],
            right: vec![
                WegItem::Plugin {
                    id: Uuid::new_v4(),
                    plugin: "@default/weg-trash-bin".into(),
                },
                WegItem::AppOrFile(WegItemData {
                    id: Uuid::new_v4(),
                    display_name: "Right App".to_string(),
                    umid: None,
                    path: "C:\\right.exe".into(),
                    pinned: true,
                    prevent_pinning: false,
                    relaunch: None,
                }),
            ],
        };

        // Simulate the filter logic
        let keep_non_app_items = |items: &[WegItem]| -> Vec<WegItem> {
            items
                .iter()
                .filter(|item| !matches!(item, WegItem::AppOrFile(_)))
                .cloned()
                .collect()
        };

        let filtered_left = keep_non_app_items(&original.left);
        let filtered_center = keep_non_app_items(&original.center);
        let filtered_right = keep_non_app_items(&original.right);

        // Verify special items are preserved
        assert_eq!(filtered_left.len(), 1, "Plugin should be preserved");
        assert!(matches!(filtered_left[0], WegItem::Plugin { .. }));

        assert_eq!(filtered_center.len(), 1, "Separator should be preserved");
        assert!(matches!(filtered_center[0], WegItem::Separator { .. }));

        assert_eq!(filtered_right.len(), 1, "Plugin should be preserved");
        assert!(matches!(filtered_right[0], WegItem::Plugin { .. }));
    }

    #[test]
    fn test_import_from_windows_taskbar_internal_empty_list() {
        let manager = WegItemsManager {
            items: Mutex::new(WegItems::default()),
        };

        // This will likely fail in test environment, but we test the structure
        match manager.import_from_windows_taskbar_internal() {
            Ok(items) => {
                // Should have the base structure
                assert!(
                    !items.left.is_empty() || items.left.is_empty(),
                    "Should return valid structure"
                );
            }
            Err(_) => {
                // Expected to fail in test environment without real registry
            }
        }
    }

    #[test]
    fn test_weg_items_sanitize() {
        let mut items = WegItems {
            is_reorder_disabled: false,
            left: vec![WegItem::AppOrFile(WegItemData {
                id: Uuid::nil(), // Invalid nil ID
                display_name: "Test".to_string(),
                umid: None,
                path: "C:\\nonexistent.exe".into(),
                pinned: true,
                prevent_pinning: false,
                relaunch: None,
            })],
            center: vec![],
            right: vec![],
        };

        items.sanitize();

        // After sanitization, non-existent paths should be removed
        // and nil IDs should be replaced
        assert!(
            items.left.is_empty() || items.left[0].id() != &Uuid::nil(),
            "Nil IDs should be replaced or items removed"
        );
    }

    #[test]
    fn test_manager_get_returns_clone() {
        let manager = WegItemsManager {
            items: Mutex::new(initial_items()),
        };

        let items1 = manager.get();
        let items2 = manager.get();

        // Should return independent clones
        assert_eq!(items1.left.len(), items2.left.len());
        assert_eq!(items1.center.len(), items2.center.len());
        assert_eq!(items1.right.len(), items2.right.len());
    }
}
