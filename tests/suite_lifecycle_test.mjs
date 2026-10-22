import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
const read = p => readFileSync(new URL('../' + p, import.meta.url), 'utf8');
test('no placeholder metrics when the optional publisher is absent', () => {
  assert.doesNotMatch(read('src/ui/svelte/fancy-toolbar/components/SuiteMetrics.svelte'), /Stats —|suite-metrics-unavailable/);
});
test('metrics access honors the persisted component switch', () => {
  assert.match(read('src/background/modules/desktop_suite/infrastructure.rs'), /stats_enabled/);
});
test('host starts enabled components after its own startup', () => {
  const main = read('src/background/main.rs');
  assert.ok(main.indexOf('start_enabled') > main.indexOf('SeelenUI::start().await?'));
});
test('central preferences expose both independent components and whole-suite exit', () => {
  const ui = read('src/ui/react/settings/modules/desktopSuite/index.tsx');
  for (const command of ['GetDesktopSuiteState', 'SetDesktopSuiteComponent', 'SetAutoStart', 'ExitDesktopSuite']) assert.ok(ui.includes(command), command);
});
test('home and general startup controls share the verified status', () => {
  const ui = read('src/ui/react/settings/modules/desktopSuite/index.tsx');
  assert.match(ui, /import\s*\{\s*autostart\s*\}\s*from/);
  assert.ok((ui.match(/autostart\.value\s*=\s*next\.autostart/g) ?? []).length === 2,
    'Both explicit refresh and background polling must publish verified startup status');
});
