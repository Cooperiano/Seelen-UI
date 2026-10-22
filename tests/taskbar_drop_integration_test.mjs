import { readFileSync } from "node:fs";
import { test } from "node:test";
import assert from "node:assert/strict";
import { move } from "@dnd-kit/helpers";
import ts from "typescript";
import { commitTaskbarDrop, withTaskbarBoundary, TASKBAR_BOUNDARY } from "../src/ui/svelte/weg/domain/taskbarDrop.ts";

test("actual dnd helper crossing the virtual separator commits pin and persists one real item", () => {
  const current = [{ id: "a", type: "AppOrFile", pinned: true }, { id: "b", type: "AppOrFile", pinned: false }];
  const preview = withTaskbarBoundary(current, true);
  const reordered = move(preview, { operation: { source: { id: "b" }, target: { id: TASKBAR_BOUNDARY } } });
  const stored = JSON.parse(JSON.stringify(commitTaskbarDrop(current, reordered, "b", true)));
  assert.deepEqual(stored, [{ ...current[0] }, { ...current[1], pinned: true }]);
  const next = withTaskbarBoundary(stored, true);
  const undone = move(next, { operation: { source: { id: "b" }, target: { id: TASKBAR_BOUNDARY } } });
  assert.equal(commitTaskbarDrop(stored, undone, "b", true)[1].pinned, false);
});
test("Dock previews locally and commits only accepted drops, rather than saving every hover", () => {
  const dock = readFileSync("src/ui/svelte/weg/components/Dock.svelte", "utf8");
  assert.match(dock, /commitTaskbarDrop/);
  assert.match(dock, /event\.canceled/);
  assert.match(dock, /getBoundingClientRect/);
  const hover = dock.slice(dock.indexOf("function handleDragOver"), dock.indexOf("function handleDragStart"));
  assert.doesNotMatch(hover, /dockState\.items\s*=/);
});
test("divider accepts drops but cannot itself be dragged, and stays available for empty regions", () => {
  const divider = readFileSync("src/ui/svelte/weg/components/TaskbarBoundary.svelte", "utf8");
  assert.match(divider, /draggable:\s*true/);
  assert.match(divider, /droppable:\s*!active/);
  assert.match(divider, /sortable\.attach/);
});

// Execute the actual component handlers with explicit DOM/store boundaries. This is
// integration evidence, not a substitute for installed pointer-input acceptance.
function handlers(initial) {
  const source = readFileSync("src/ui/svelte/weg/components/Dock.svelte", "utf8").split('<script lang="ts">')[1].split("</script>")[0];
  const parsed = ts.createSourceFile("Dock.ts", source, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS);
  const functions = parsed.statements.filter(n => ts.isFunctionDeclaration(n) && /^handleDrag/.test(n.name?.text ?? "")).map(n => n.getText(parsed)).join("\n");
  const js = ts.transpileModule(functions, { compilerOptions: { target: ts.ScriptTarget.ES2022 } }).outputText;
  let writes = 0;
  let stored = structuredClone(initial);
  const state = { isReorderDisabled: false, get items() { return stored; }, set items(value) { writes++; stored = JSON.parse(JSON.stringify(value)); } };
  const api = new Function("move", "dockState", "withTaskbarBoundary", "commitTaskbarDrop", "setDockIsDraggingItem", `
    let previewItems = null, dragSourceId = null;
    const taskbarElement = { getBoundingClientRect: () => ({ left: 0, right: 1000, top: 0, bottom: 60 }) };
    ${js}
    return { start: handleDragStart, over: handleDragOver, end: handleDragEnd, preview: () => previewItems };
  `)(move, state, withTaskbarBoundary, commitTaskbarDrop, () => {});
  return { ...api, state, writes: () => writes };
}
const initial = [{ id: "a", type: "AppOrFile", pinned: true }, { id: "b", type: "AppOrFile", pinned: false }];
function drag(api, canceled = false, inside = true) {
  const operation = { source: { id: "b" }, target: { id: TASKBAR_BOUNDARY }, position: { current: { x: 500, y: inside ? 30 : 300 } } };
  api.start({ operation });
  api.over({ operation });
  assert.equal(api.writes(), 0, "hover must not save intermediate membership/order");
  api.end({ operation, canceled });
}
test("actual component handlers commit on release and reloaded state keeps cross-line pin", () => {
  const api = handlers(initial);
  drag(api);
  assert.equal(api.writes(), 1);
  assert.equal(api.state.items[1].pinned, true);
  assert.equal(api.preview(), null);
  assert.deepEqual(handlers(api.state.items).state.items, [{ ...initial[0] }, { ...initial[1], pinned: true }]);
});
test("actual Esc, off-Dock release and reorder lock commit no state", () => {
  for (const [cancel, inside] of [[true, true], [false, false]]) {
    const api = handlers(initial);
    drag(api, cancel, inside);
    assert.equal(api.writes(), 0);
    assert.deepEqual(api.state.items, initial);
  }
  const locked = handlers(initial);
  locked.state.isReorderDisabled = true;
  drag(locked);
  assert.equal(locked.writes(), 0);
});
