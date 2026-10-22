import { readFileSync } from 'node:fs';
import './taskbar_visibility_test.mjs';
import assert from 'node:assert/strict';
import test from 'node:test';
const source = p => readFileSync(new URL('../'+p, import.meta.url), 'utf8');
test('live application sessions reach retention, visibility and the running indicator without becoming windows',()=>{
  assert.match(source('src/ui/svelte/weg/state/getters.svelte.ts'),/GetWegAppSessions/);
  assert.match(source('src/ui/svelte/weg/state/items.svelte.ts'),/reconcileApplications\(appOrFileItems, windows, appSessions.value\)/);
  assert.match(source('src/ui/svelte/weg/components/Dock.svelte'),/appSessions.value/);
  assert.match(source('src/ui/svelte/weg/components/items/UserApplicationItem.svelte'),/aria-label=\{item.displayName\}/);
  assert.match(source('src/ui/svelte/weg/components/items/UserApplicationItem.svelte'),/isApplicationRunning/);
});
test('a repeated pin uses the existing application slot',()=>{
  assert.match(source('src/ui/svelte/weg/state/items.svelte.ts'),/mergeApplicationInPlace/);
});
test('importing Windows pins cannot clear the custom taskbar application list',()=>{
  const code=source('src/background/state/application/weg_items.rs').split('pub fn import_from_windows_taskbar(&self)')[1].split('fn initial_items')[0];
  assert.ok(!code.includes('keep_non_app_items'), 'import still clears user apps before rebuilding');
  assert.ok(code.includes('matches_application'), 'import needs identity-aware in-place matching');
});
test('unpinning a windowless but running app does not remove its live icon',()=>{
  assert.ok(source('src/ui/svelte/weg/appMenu.ts').includes('getWindowsForItem(item, appSessions.value)'), 'unpin treats no windows as an exited application');
});
import './dock_reveal_test.mjs';
