import { reconcileApplications, mergeApplicationInPlace } from "./reconcileApplications.ts";
declare const Deno: { test(name: string, test: () => void): void };
const pin = { id: "saved", pinned: true, umid: "app", path: "app.exe" };
const session = { hwnd: 0, umid: "app", process: { id: 10, path: "app.exe" } };
function check(value: boolean) { if (!value) throw new Error("Application lifecycle regression"); }

Deno.test("windowless known running app retains its temporary item until the owning process exits", () => {
  const item = { ...pin, pinned: false };
  check(reconcileApplications([item], [], [session]).itemsToRemove.size === 0);
  check(reconcileApplications([item], [], []).itemsToRemove.has(item.id));
});
Deno.test("a live pinned application is not duplicated when the same app is pinned again", () => {
  const result = mergeApplicationInPlace([pin], { ...pin, id: "new" }, [session]);
  check(result.length === 1 && result[0]!.id === "saved" && result[0]!.pinned);
});
Deno.test("pinning a temporary application preserves its existing slot and identifier", () => {
  const first = { ...pin, id: "first", umid: "other", path: "other.exe" };
  const temporary = { ...pin, pinned: false };
  const result = mergeApplicationInPlace([first, temporary], { ...pin, id: "new" }, [session]);
  check(result[0] === first && result[1]!.id === temporary.id && result[1]!.pinned);
});
Deno.test("distinct explicit identities sharing an executable must never merge", () => {
  const result = mergeApplicationInPlace([pin], { ...pin, id: "new", umid: "profile-b" }, [session]);
  check(result.length === 2);
});
Deno.test("new running applications do not reorder existing user positions", () => {
  const second = { ...pin, id: "second", umid: "second" };
  const result = mergeApplicationInPlace([second, pin], { ...pin, id: "third", umid: "third" }, []);
  check(result.map((i: typeof pin) => i.id).join(",") === "second,saved,third");
});
