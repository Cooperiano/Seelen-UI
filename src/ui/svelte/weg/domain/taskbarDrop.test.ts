import { commitTaskbarDrop, TASKBAR_BOUNDARY as B, withTaskbarBoundary, type TaskbarDropItem } from "./taskbarDrop.ts";
declare const Deno: { test(name: string, test: () => void): void };
function eq(actual: unknown, expected: unknown) {
  if (JSON.stringify(actual) !== JSON.stringify(expected)) throw new Error(`${JSON.stringify(actual)} != ${JSON.stringify(expected)}`);
}
const app = (id: string, pinned: boolean): TaskbarDropItem => ({ id, type: "AppOrFile", pinned });
const boundary: TaskbarDropItem = { id: B, type: "Separator" };
const plugin: TaskbarDropItem = { id: "trash", type: "Plugin" };
const ids = (items: TaskbarDropItem[]) => items.map(i => i.id);

Deno.test("drop across boundary pins at requested position and never saves synthetic divider", () => {
  const current = [app("a", true), app("b", true), app("c", false), plugin];
  const result = commitTaskbarDrop(current, [current[0]!, current[2]!, current[1]!, boundary, plugin], "c", true);
  eq(ids(result), ["a", "c", "b", "trash"]);
  eq(result.find(i => i.id === "c")?.pinned, true);
  eq(current[2]!.pinned, false);
});
Deno.test("drop to running section unpins in that section's requested order", () => {
  const current = [app("a", true), app("b", true), app("c", false), app("d", false), plugin];
  const result = commitTaskbarDrop(current, [current[1]!, boundary, current[2]!, current[0]!, current[3]!, plugin], "a", true);
  eq(ids(result), ["b", "c", "a", "d", "trash"]);
  eq(result.find(i => i.id === "a")?.pinned, false);
});
Deno.test("within-section reorder retains pin flags and survives serialization/reload", () => {
  const current = [app("a", true), app("b", true), app("c", false), plugin];
  const result = commitTaskbarDrop(current, [current[1]!, current[0]!, boundary, current[2]!, plugin], "b", true);
  eq(JSON.parse(JSON.stringify(result)), [app("b", true), app("a", true), app("c", false), plugin]);
});
Deno.test("cancel, outside drop and malformed previews do not commit or change membership", () => {
  const current = [app("a", true), app("b", false)];
  for (const [preview, accepted] of [[current, true], [[current[1]!, current[0]!, boundary], false], [[boundary, boundary, ...current], true], [[boundary, current[0]!, current[0]!, current[1]!], true]] as [TaskbarDropItem[], boolean][]) {
    eq(commitTaskbarDrop(current, preview, "b", accepted), current);
  }
  eq(commitTaskbarDrop(current, [boundary, ...current], "missing", true), current);
  eq(commitTaskbarDrop(current, [boundary, current[0]!], "b", true), current);
});
Deno.test("preventPinning and session-only identities cannot be fixed by dragging", () => {
  for (const extra of [{ preventPinning: true }, { sessionWindow: { hwnd: 1 } }]) {
    const current = [app("a", true), { ...app("b", false), ...extra }, plugin];
    const result = commitTaskbarDrop(current, [current[1]!, current[0]!, boundary, plugin], "b", true);
    eq(result.find(i => i.id === "b")?.pinned, false);
    eq(ids(result), ["a", "b", "trash"]);
  }
});
Deno.test("a running app can create first pin and the last pin can be removed", () => {
  const run = [app("b", false), plugin];
  eq(ids(withTaskbarBoundary(run, true)), [B, "b", "trash"]);
  const pinned = commitTaskbarDrop(run, [run[0]!, boundary, plugin], "b", true);
  eq(pinned[0]!.pinned, true);
  eq(ids(withTaskbarBoundary(pinned, true)), ["b", B, "trash"]);
  eq(commitTaskbarDrop(pinned, [boundary, pinned[0]!, plugin], "b", true)[0]!.pinned, false);
});
Deno.test("idle divider appears only between nonempty app regions; plugins keep their places", () => {
  for (const input of [[], [plugin], [app("a", true)], [app("b", false)]]) eq(withTaskbarBoundary(input, false), input);
  eq(withTaskbarBoundary([plugin], true), [plugin]);
  const start = { id: "start", type: "Plugin" };
  eq(ids(withTaskbarBoundary([start, app("a", true), app("b", false), plugin], false)), ["start", "a", B, "b", "trash"]);
});
Deno.test("latest objects and new live apps survive a drag; closed apps are not resurrected", () => {
  const original = [app("a", true), app("b", false), app("closed", false), plugin];
  const latest = [{ ...original[0]!, displayName: "new" }, original[1]!, app("new", false), plugin];
  const result = commitTaskbarDrop(latest, [original[1]!, original[0]!, boundary, original[2]!, plugin], "b", true);
  eq(ids(result), ["b", "a", "new", "trash"]);
  eq(result[1], latest[0]);
  eq(commitTaskbarDrop(latest.filter(i => i.id !== "b"), [original[1]!, boundary, original[0]!, plugin], "b", true), latest.filter(i => i.id !== "b"));
});
Deno.test("non-app reordering cannot turn plugins into pinned apps", () => {
  const current = [app("a", true), app("b", false), plugin];
  const result = commitTaskbarDrop(current, [plugin, current[0]!, boundary, current[1]!], "trash", true);
  eq(result, [plugin, ...current.slice(0, 2)]);
});
