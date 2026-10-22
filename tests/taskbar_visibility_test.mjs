import { readFileSync } from 'node:fs';
import assert from 'node:assert/strict';
import test from 'node:test';
import vm from 'node:vm';
import ts from 'typescript';

const source = p => readFileSync(new URL('../' + p, import.meta.url), 'utf8');
// Execute the real renderer heartbeat body, not a copied visibility formula.
function pulse(overrides = {}) {
  const app = source('src/ui/svelte/weg/App.svelte');
  const body = app.split('const pulse = () => {')[1]?.split('const timer = setInterval')[0];
  assert.ok(body, 'Actual renderer pulse must be present');
  let reported;
  const values = {
    mounted: true, initialized: true, hasItems: true, showWidget: true,
    hidden: false, hiddenByPolicy: false, opacity: '1', visibility: 'visible',
    mode: 'Never', width: 900, height: 60, top: 100, bottom: 160, ...overrides,
  };
  const surface = {
    classList: { contains: () => values.hidden },
    getBoundingClientRect: () => values,
    querySelector: () => values.hasItems ? {} : null,
  };
  const code = ts.transpile(`const pulse=()=>{${body} pulse();`, { target: ts.ScriptTarget.ES2022 });
  vm.runInNewContext(code, {
    document: { querySelector: () => values.mounted ? surface : null },
    getComputedStyle: () => values,
    settingsState: { isReady: values.initialized, hideMode: values.mode },
    dockShouldBeHidden: { value: values.hiddenByPolicy },
    HideMode: { Never: 'Never', Always: 'Always', OnOverlap: 'OnOverlap' },
    innerHeight: 261, showWidget: values.showWidget,
    SeelenCommand: { DesktopSuiteTaskbarHeartbeat: 'pulse' },
    invoke: (_command, request) => { reported = request.ready; return Promise.resolve(); },
  });
  return reported;
}

test('visible mounted Dock renews the native replacement lease', () => assert.equal(pulse(), true));
test('intentional auto-hide remains healthy outside the visible viewport', () => {
  assert.equal(pulse({ mode: 'Always', hidden: true, hiddenByPolicy: true, opacity: '0', top: 280, bottom: 340 }), true);
});
test('an accidentally hidden always-visible Dock fails closed', () => {
  assert.equal(pulse({ hidden: true, hiddenByPolicy: true, opacity: '0', top: 280, bottom: 340 }), false);
});
test('unexpected CSS hiding in auto-hide mode is not healthy', () => {
  assert.equal(pulse({ mode: 'Always', hidden: true, hiddenByPolicy: false, opacity: '0' }), false);
});
for (const [label, value] of Object.entries({ mounted: false, initialized: false, hasItems: false, showWidget: false, width: 0, height: 0 })) {
  test(`intentional hiding cannot conceal missing ${label}`, () => {
    assert.equal(pulse({ mode: 'Always', hidden: true, hiddenByPolicy: true, opacity: '0', [label]: value }), false);
  });
}
test('the suite has no independent replacement switch competing with Dock display mode', () => {
  assert.doesNotMatch(source('src/ui/react/settings/modules/desktopSuite/index.tsx'), /SetDesktopSuiteTaskbar/);
});
test('the native guard does not read a second user-visible replacement preference', () => {
  assert.doesNotMatch(source('src/background/modules/desktop_suite/taskbar_guard.rs'), /taskbar-takeover\.json/);
});
test('legacy overlap mode is normalized at the actual save boundary, not only default creation', () => {
  const save = source('src/background/state/infrastructure.rs').split('pub fn state_write_settings(')[1]?.split('pub fn state_get_settings_by_app')[0];
  assert.match(save, /normalize_suite_settings\(&mut settings\)/);
});
