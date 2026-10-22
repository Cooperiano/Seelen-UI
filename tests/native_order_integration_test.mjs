import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import "./taskbar_drop_integration_test.mjs";
const read = p => readFileSync(new URL(p, import.meta.url), "utf8");
const manager = read("../src/background/state/application/weg_items.rs");
const method = manager.slice(manager.indexOf("pub fn migrate_native_order_once"),manager.indexOf("pub fn get(&self)"));
test("migration is explicit, serialized, validated, backed up and receipt-protected",()=>{
  for(const text of ["self.items.lock()", "may_migrate(receipt.exists())", "get_windows_taskbar_pinned_apps()?", "plan_order(", "migrated.sanitize()", "Native pin validation failed", "before-native-order-", '"status":"prepared"', '"status":"complete"']) assert.ok(method.includes(text),text);
  assert.ok(method.indexOf("may_migrate")<method.indexOf("get_windows_taskbar"));
  assert.ok(method.indexOf('atomic_write_file(&backup')<method.indexOf('atomic_write_file(&receipt'));
  assert.ok(method.indexOf('atomic_write_file(&receipt')<method.indexOf('atomic_write_file(&path'));
  assert.ok(method.indexOf('atomic_write_file(&path')<method.indexOf('*guard = migrated'));
  const load=manager.slice(manager.indexOf("pub fn load(&self)"),manager.indexOf("/// Internal method"));
  assert.ok(!load.includes("migrate_native_order_once"));
});
test("full native inventory uses read-only Shell enumeration, preserving packaged and Unicode targets",()=>{
  const native=read("../src/background/widgets/weg/native_taskbar.rs");
  const shell=read("../src/background/widgets/weg/native_pins.rs");
  assert.match(native,/native_pins::read\(\)\?/);
  assert.match(native,/umid: Some\(parsing\)/);
  assert.match(shell,/SIGDN_DESKTOPABSOLUTEPARSING/);
  assert.match(shell,/fetched != 1/);
  assert.ok(!shell.includes("Modify("));
  assert.ok(!shell.includes("filter_map"));
});
test("actual registered work area excludes duplicate inner margin without shrinking click hitbox",()=>{
  const settings=read("../src/ui/svelte/weg/state/settings.svelte.ts");
  assert.match(settings,/rect: dockReservation\(hitboxRect, position, settings.margin, systemState.currentMonitor.scaleFactor\)/);
  assert.match(settings,/getPhysicalRect:[\s\S]*widgetRect.webviewRect/);
});
test("rendered sections and drag indices use the same order, divider is not persisted",()=>{
  const items=read("../src/ui/svelte/weg/state/items.svelte.ts");
  const dock=read("../src/ui/svelte/weg/components/Dock.svelte");
  const group=read("../src/ui/svelte/weg/components/DockItemsGroup.svelte");
  assert.match(items,/presentedItems = \$derived\(orderTaskbarSections\(_dockState.items\)\)/);
  assert.match(items,/return presentedItems/);
  assert.match(dock,/withTaskbarBoundary\(dockState.items/);
  assert.match(dock,/displayedItems.forEach/);
  assert.match(group,/item.id === TASKBAR_BOUNDARY/);
  assert.match(group,/<TaskbarBoundary/);
  assert.match(dock,/commitTaskbarDrop\(dockState.items/);
  assert.ok(!items.includes('weg-application-section-divider'));
});
