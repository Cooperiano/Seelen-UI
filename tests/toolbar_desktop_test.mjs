import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';
import ts from 'typescript';

// Run the actual component script. Lazy derived getters and an explicit effect
// flush replace only Svelte scheduling; native transport never touches the OS.
const source = readFileSync(new URL('../src/ui/svelte/fancy-toolbar/App.svelte', import.meta.url), 'utf8')
  .match(/<script lang="ts">([\s\S]*?)<\/script>/)[1];
const parsed = ts.createSourceFile('toolbar.ts', source, ts.ScriptTarget.Latest, true);
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

function fixture({ready = true, fullscreen = false} = {}) {
  const calls = [], effects = [];
  let mount;
  const focused = {value: {hwnd: 1, class: 'Chrome_WidgetWin_1', exe: 'C:\\Apps\\browser.exe'}};
  const settingsState = {isReady: ready};
  const windowsState = {topInteractableWindow: {isFullscreen: fullscreen}};
  const native = async (command, args) => {calls.push([command, args]);};
  const context = {exports: {}, focused, settingsState, windowsState, widgetStatuses: {value: []},
    Widget: {self: {show: () => native('show'), hide: () => native('hide'), ready: async () => {}}},
    invoke: native, SeelenCommand: {SetSelfZOrder: 'restack'}, ZOrder: {TopMost: 'TopMost'},
    debounce: fn => fn, $effect: fn => effects.push(fn), onMount: fn => {mount = fn;}, console};
  vm.runInNewContext(compiled, context, {filename: 'toolbar-desktop-component.js'});
  return {calls, focused, settingsState, windowsState, context,
    async flush() {for (const effect of effects) effect(); await new Promise(resolve => setImmediate(resolve));},
    async mount() {mount(); await new Promise(resolve => setImmediate(resolve));},
    desktop(cls = 'Progman') {focused.value = {hwnd: 99, class: cls, exe: 'C:\\Windows\\explorer.exe'};}};
}

test('Show Desktop reasserts the existing toolbar layer even when visibility stayed true', async () => {
  const f = fixture(); await f.flush(); f.calls.length = 0;
  f.desktop(); await f.flush();
  assert.deepEqual(f.calls.map(c => c[0]), ['show', 'restack']);
  assert.equal(f.calls[1][1].zOrder, 'TopMost');
});
test('desktop foreground wins over stale background fullscreen state', async () => {
  for (const cls of ['Progman', 'WorkerW']) {
    const f = fixture({fullscreen: true}); f.desktop(cls); await f.flush();
    assert.deepEqual(f.calls.map(c => c[0]), ['show', 'restack']);
  }
});
test('genuine foreground fullscreen stays hidden, without any restack', async () => {
  const f = fixture({fullscreen: true}); await f.flush();
  assert.deepEqual(f.calls.map(c => c[0]), ['hide']);
});
test('ordinary applications and shell search do not receive desktop restacking', async () => {
  const f = fixture(); await f.flush();
  assert.deepEqual(f.calls.map(c => c[0]), ['show']);
  f.calls.length = 0; f.windowsState.topInteractableWindow.isFullscreen = true;
  f.focused.value = {hwnd: 12, class: 'Windows.UI.Core.CoreWindow', exe: 'C:\\Windows\\SearchHost.exe'};
  await f.flush(); assert.deepEqual(f.calls.map(c => c[0]), ['show']);
});
test('not-ready toolbar does not show, restack or start another component', async () => {
  const f = fixture({ready: false}); f.desktop(); await f.flush(); assert.deepEqual(f.calls, []);
});
test('leaving desktop for fullscreen restores normal fullscreen hiding', async () => {
  const f = fixture({fullscreen: true}); f.desktop(); await f.flush(); f.calls.length = 0;
  f.focused.value = {hwnd: 1, class: 'GameWindow', exe: 'C:\\Apps\\game.exe'};
  await f.flush(); assert.deepEqual(f.calls.map(c => c[0]), ['hide']);
});
test('an application imitating a desktop class is not treated as Explorer desktop', async () => {
  const f = fixture({fullscreen: true});
  f.focused.value = {hwnd: 4, class: 'Progman', exe: 'C:\\Apps\\other.exe'};
  await f.flush(); assert.deepEqual(f.calls.map(c => c[0]), ['hide']);
});
test('focus changing during show does not restack above the newer fullscreen app', async () => {
  const f = fixture(); f.desktop();
  let resolveShow;
  f.context.Widget.self.show = () => new Promise(resolve => {resolveShow = resolve;});
  const pending = f.flush();
  f.focused.value = {hwnd: 4, class: 'GameWindow', exe: 'C:\\Apps\\game.exe'};
  resolveShow(); await pending; assert.deepEqual(f.calls, []);
});
test('a native visibility failure is caught and never followed by a restack', async () => {
  const f = fixture(); f.desktop(); const errors = [];
  f.context.console = {error: (...args) => errors.push(args)};
  f.context.Widget.self.show = async () => {throw new Error('window unavailable');};
  await f.flush(); assert.deepEqual(f.calls, []); assert.equal(errors.length, 1);
});
test('native widget readiness still gates the first desktop restoration', async () => {
  const f = fixture({ready: false}); f.desktop(); await f.flush(); assert.deepEqual(f.calls, []);
  await f.mount(); assert.equal(f.settingsState.isReady, true);
  await f.flush(); assert.deepEqual(f.calls.map(c => c[0]), ['show', 'restack']);
});
test('existing custom apps-menu exemption remains functional without desktop restacking', async () => {
  const f = fixture({fullscreen: true});
  f.context.widgetStatuses.value = [{widgetId: '@other/popup', webviewWindowId: 0}, {widgetId: '@seelen/apps-menu', webviewWindowId: 1}];
  await f.flush(); assert.deepEqual(f.calls.map(c => c[0]), ['show']);
});
test('missing focus metadata is not enough to declare desktop visible over fullscreen', async () => {
  const f = fixture({fullscreen: true}); f.focused.value = null;
  await f.flush(); assert.deepEqual(f.calls.map(c => c[0]), ['hide']);
});
