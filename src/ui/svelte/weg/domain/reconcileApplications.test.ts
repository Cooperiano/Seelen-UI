import { reconcileApplications } from "./reconcileApplications.ts";

declare const Deno: { test(name: string, test: () => void): void };

const pin = { id: "pin", pinned: true, umid: null, path: "c:/app.exe" };
const live = { hwnd: 1, umid: "app", process: { id: 10, path: "C:\\App.exe" }, isIconic: false };
function assert(condition: boolean) {
  if (!condition) throw new Error("Dock reconciliation regression");
}

Deno.test("closed saved pins remain visible and in their saved order", () => {
  const second = { ...pin, id: "second", path: "other.exe" };
  const items = [pin, second];
  const delta = reconcileApplications(items, []);
  assert(delta.itemsToRemove.size === 0 && delta.uncoveredWindows.length === 0);
  assert(items[0] === pin && items[1] === second);
});
Deno.test("closing the last unpinned window removes its temporal item", () => {
  assert(reconcileApplications([{ ...pin, pinned: false }], []).itemsToRemove.has(pin.id));
});
Deno.test("legacy pin covers multiple live windows without duplicate app entries", () => {
  assert(reconcileApplications([pin], [live, { ...live, hwnd: 2 }]).uncoveredWindows.length === 0);
});
Deno.test("minimized windows stay in the Dock inventory", () => {
  const minimized = { ...live, isIconic: true };
  const delta = reconcileApplications([{ ...pin, pinned: false }], [minimized]);
  assert(delta.itemsToRemove.size === 0 && delta.uncoveredWindows.length === 0);
});
Deno.test("distinct app IDs remain uncovered by an ambiguous executable-only pin", () => {
  const delta = reconcileApplications([pin], [live, { ...live, hwnd: 2, umid: "other-profile" }]);
  assert(delta.itemsToRemove.size === 0 && delta.uncoveredWindows.length === 2);
});
Deno.test("identity-less live windows remain uncovered for session item creation", () => {
  const delta = reconcileApplications([], [{ ...live, umid: null, process: { id: 10, path: null } }]);
  assert(delta.uncoveredWindows.length === 1);
});
Deno.test("session item covers its window and is removed after closure", () => {
  const window = { ...live, umid: null, process: { id: 10, path: null } };
  const session = { ...pin, pinned: false, path: "", sessionWindow: { hwnd: 1, pid: 10 } };
  assert(reconcileApplications([session], [window]).uncoveredWindows.length === 0);
  assert(reconcileApplications([session], []).itemsToRemove.has(session.id));
});
Deno.test("repeated inventory updates do not create more entries after reconciliation", () => {
  const entry = { ...pin, id: "running", pinned: false, umid: live.umid };
  for (let i = 0; i < 10; i++) {
    const delta = reconcileApplications([entry], [live]);
    assert(delta.itemsToRemove.size === 0 && delta.uncoveredWindows.length === 0);
  }
});

Deno.test("resolved executable metadata replaces the session placeholder with a normal application entry", () => {
  const session = { ...pin, pinned: false, path: "", sessionWindow: { hwnd: 1, pid: 10 } };
  const delta = reconcileApplications([session], [live]);
  assert(delta.itemsToRemove.has(session.id) && delta.uncoveredWindows[0] === live);
});

Deno.test("taskbar multi-window lifecycle keeps the item until the last window closes and discovers reopening", () => {
  const running = { ...pin, id: "running", pinned: false, umid: live.umid };
  for (const inventory of [[live, { ...live, hwnd: 2 }], [live]]) {
    const delta = reconcileApplications([running], inventory);
    assert(delta.itemsToRemove.size === 0 && delta.uncoveredWindows.length === 0);
  }
  assert(reconcileApplications([running], []).itemsToRemove.has(running.id));
  const reopened = { ...live, hwnd: 3, process: { ...live.process, id: 11 } };
  assert(reconcileApplications([], [reopened]).uncoveredWindows[0] === reopened);
});

Deno.test("taskbar saved pin survives close and covers a reopened process without creating a second application", () => {
  assert(reconcileApplications([pin], []).itemsToRemove.size === 0);
  const reopened = { ...live, hwnd: 3, process: { ...live.process, id: 11 } };
  const delta = reconcileApplications([pin], [reopened]);
  assert(delta.itemsToRemove.size === 0 && delta.uncoveredWindows.length === 0);
});

Deno.test("taskbar session placeholder never adopts a reused window handle from another process", () => {
  const session = { ...pin, pinned: false, path: "", sessionWindow: { hwnd: 1, pid: 10 } };
  const replacement = { ...live, umid: null, process: { id: 11, path: null } };
  const delta = reconcileApplications([session], [replacement]);
  assert(delta.itemsToRemove.has(session.id) && delta.uncoveredWindows[0] === replacement);
});

Deno.test("taskbar reconciliation does not mutate saved order or caller window inventory", () => {
  const items = Object.freeze([Object.freeze({ ...pin }), Object.freeze({ ...pin, id: "second", path: "other.exe" })]);
  const inventory = Object.freeze([Object.freeze({ ...live }), Object.freeze({ ...live, hwnd: 2 })]);
  const beforeItems = JSON.stringify(items);
  const beforeWindows = JSON.stringify(inventory);
  reconcileApplications(items, inventory);
  assert(JSON.stringify(items) === beforeItems && JSON.stringify(inventory) === beforeWindows);
});
