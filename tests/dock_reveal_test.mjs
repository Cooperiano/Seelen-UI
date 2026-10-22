import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';
import ts from 'typescript';

// Exercise the real component script; only the scheduler and native transport
// are replaced. No OS input, focus, settings, or shell writes occur in tests.
const source = readFileSync(new URL('../src/ui/svelte/weg/App.svelte', import.meta.url), 'utf8')
  .match(/<script lang="ts">([\s\S]*?)<\/script>/)[1];
const parsed = ts.createSourceFile('dock.ts', source, ts.ScriptTarget.Latest, true);
const derived = new Set(parsed.statements.flatMap(s => ts.isVariableStatement(s)
  ? s.declarationList.declarations.filter(d => ts.isCallExpression(d.initializer) && d.initializer.expression.getText(parsed) === '$derived').map(d => d.name.text)
  : []));
const compiled = ts.transpileModule(source, {compilerOptions: {target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext},
  transformers: {before: [context => root => {
    const visit = node => {
      if (ts.isImportDeclaration(node)) return undefined;
      if (ts.isVariableStatement(node) && node.declarationList.declarations.length === 1) {
        const d = node.declarationList.declarations[0];
        if (derived.has(d.name.text)) return ts.factory.createFunctionDeclaration(undefined, undefined, d.name.text, undefined, [], undefined,
          ts.factory.createBlock([ts.factory.createReturnStatement(ts.visitNode(d.initializer.arguments[0], visit))]));
      }
      if (ts.isIdentifier(node) && derived.has(node.text) && !(ts.isPropertyAccessExpression(node.parent) && node.parent.name === node))
        return ts.factory.createCallExpression(ts.factory.createIdentifier(node.text), undefined, []);
      return ts.visitEachChild(node, visit, context);
    };
    return ts.visitNode(root, visit);
  }]}}).outputText.replace(/^export \{\};?\s*$/gm, '');

function fixture({ready = true, fullscreen = false, edge = null, position = 'Bottom'} = {}) {
  const calls = [], effects = [], errors = [];
  const focused = {value: {hwnd: 1, class: 'Chrome_WidgetWin_1', exe: 'C:\\Apps\\browser.exe'}};
  const settingsState = {isReady: ready, position, hideMode: 'Always'};
  const windowsState = {topInteractableWindow: {isFullscreen: fullscreen}};
  const systemState = {mouseAtEdge: edge};
  let mount, pulse, stopped = false;
  const native = async (command, args) => {calls.push([command, args]);};
  const context = {focused, settingsState, windowsState, systemState, widgetStatuses: {value: []},
    dockShouldBeHidden: {value: false}, HideMode: {Always: 'Always'},
    Widget: {self: {show: () => native('show'), hide: () => native('hide'), ready: async () => {}}},
    invoke: native, SeelenCommand: {SetSelfZOrder: 'restack', DesktopSuiteTaskbarHeartbeat: 'heartbeat'}, ZOrder: {TopMost: 'TopMost'},
    debounce: fn => fn, $effect: fn => effects.push(fn), onMount: fn => {mount = fn;},
    setInterval: fn => {pulse = fn; return 1;}, clearInterval: () => {stopped = true;},
    document: {querySelector: () => null}, innerHeight: 261,
    console: {error: (...args) => errors.push(args)}};
  vm.runInNewContext(compiled, context, {filename: 'dock-reveal-component.js'});
  return {calls, settingsState, systemState, windowsState, context, errors,
    get stopped() {return stopped;}, pulse() {pulse();},
    async mount() {const cleanup = mount(); await new Promise(resolve => setImmediate(resolve)); return cleanup;},
    async flush() {for (const effect of effects) effect(); await new Promise(resolve => setImmediate(resolve));}};
}

test('edge entry reasserts Dock layer even when its native visible flag was already true', async () => {
  const f = fixture(); await f.flush(); f.calls.length = 0;
  f.systemState.mouseAtEdge = 'Bottom'; await f.flush();
  assert.deepEqual(f.calls.map(c => c[0]), ['show', 'restack']);
  assert.equal(f.calls[1][1].zOrder, 'TopMost');
});
test('each configured Dock edge can reveal without changing its settings', async () => {
  for (const position of ['Top', 'Bottom', 'Left', 'Right']) {
    const f = fixture({position, edge: position}); await f.flush();
    assert.deepEqual(f.calls.map(c => c[0]), ['show', 'restack']);
    assert.equal(f.settingsState.hideMode, 'Always');
  }
});
test('wrong edge and ordinary movement never restack the Dock', async () => {
  for (const edge of [null, 'Top', 'Left', 'Right']) {
    const f = fixture({edge}); await f.flush();
    assert.deepEqual(f.calls.map(c => c[0]), ['show']);
  }
});
test('genuine fullscreen and not-ready Dock never restack', async () => {
  const f = fixture({fullscreen: true, edge: 'Bottom'}); await f.flush();
  assert.deepEqual(f.calls.map(c => c[0]), ['hide']);
  const g = fixture({ready: false, edge: 'Bottom'}); await g.flush(); assert.deepEqual(g.calls, []);
});
test('leaving the edge during native show cancels late restacking', async () => {
  const f = fixture({edge: 'Bottom'}); let finish;
  f.context.Widget.self.show = () => new Promise(resolve => {finish = resolve;});
  const pending = f.flush(); f.systemState.mouseAtEdge = null; finish(); await pending;
  assert.deepEqual(f.calls, []);
});
test('new fullscreen focus during native show cancels late restacking', async () => {
  const f = fixture({edge: 'Bottom'}); let finish;
  f.context.Widget.self.show = () => new Promise(resolve => {finish = resolve;});
  const pending = f.flush(); f.windowsState.topInteractableWindow.isFullscreen = true; finish(); await pending;
  assert.deepEqual(f.calls, []);
});
test('moving the Dock or losing readiness during show cancels late restacking', async () => {
  for (const change of [f => {f.settingsState.position = 'Left';}, f => {f.settingsState.isReady = false;}]) {
    const f = fixture({edge: 'Bottom'}); let finish;
    f.context.Widget.self.show = () => new Promise(resolve => {finish = resolve;});
    const pending = f.flush(); change(f); finish(); await pending;
    assert.deepEqual(f.calls, []);
  }
});
test('the reused native layer transport retains no-activation window positioning', () => {
  const native = readFileSync(new URL('../src/background/windows_api/mod.rs', import.meta.url), 'utf8');
  const position = native.slice(native.indexOf('pub fn set_position('), native.indexOf('pub fn move_window('));
  assert.match(position, /\| SWP_NOACTIVATE/);
  const order = native.slice(native.indexOf('pub fn set_z_order('), native.indexOf('pub fn get_foreground_window('));
  assert.match(order, /Self::set_position\(hwnd, Some\(order\)/);
  assert.doesNotMatch(order, /Self::set_foreground\(/);
});
test('native show failure is caught and cannot be followed by restacking', async () => {
  const f = fixture({edge: 'Bottom'});
  f.context.Widget.self.show = async () => {throw new Error('native unavailable');};
  await f.flush(); assert.deepEqual(f.calls, []); assert.equal(f.errors.length, 1);
});
test('shell search and custom apps menu keep the existing fullscreen exemption', async () => {
  const f = fixture({fullscreen: true, edge: 'Bottom'});
  f.context.focused.value.exe = 'C:\\Windows\\SearchHost.exe'; await f.flush();
  assert.deepEqual(f.calls.map(c => c[0]), ['show', 'restack']);
  f.calls.length = 0; f.context.focused.value.exe = 'C:\\Apps\\other.exe';
  f.context.widgetStatuses.value = [{widgetId: '@other/popup', webviewWindowId: 1}, {widgetId: '@seelen/apps-menu', webviewWindowId: 1}];
  await f.flush(); assert.deepEqual(f.calls.map(c => c[0]), ['show', 'restack']);
});
test('edge reveal cannot bypass widget readiness or the native taskbar health lease', async () => {
  const f = fixture({ready: false, edge: 'Bottom'});
  await f.flush(); assert.deepEqual(f.calls, []);
  const cleanup = await f.mount(); assert.equal(f.settingsState.isReady, true);
  f.pulse(); await new Promise(resolve => setImmediate(resolve));
  assert.equal(f.calls.at(-1)[0], 'heartbeat'); assert.equal(f.calls.at(-1)[1].ready, false);
  f.context.document.querySelector = () => ({getBoundingClientRect: () => ({top: 200, bottom: 260, width: 300, height: 60}),
    classList: {contains: () => false}, querySelector: () => ({})});
  f.context.getComputedStyle = () => ({visibility: 'visible', opacity: '1'});
  f.pulse(); await new Promise(resolve => setImmediate(resolve));
  assert.equal(f.calls.at(-1)[1].ready, true);
  cleanup(); assert.equal(f.stopped, true);
});
