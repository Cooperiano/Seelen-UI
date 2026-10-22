import {
  canLaunchDockApplication,
  type DockApplicationIdentity,
  matchesDockApplication,
  runningApplicationKey,
  type WindowApplicationIdentity,
} from "./applicationIdentity.ts";

declare const Deno: { test(name: string, test: () => void): void };

function assert(value: boolean) {
  if (!value) throw new Error("Identity regression");
}
function item(umid: string | null, path = "C:\\App.exe"): DockApplicationIdentity {
  return { umid, path };
}
function window(umid: string | null, path: string | null = "c:/app.exe", hwnd = 1): WindowApplicationIdentity {
  return { umid, hwnd, process: { id: 10, path } };
}

Deno.test("explicit app IDs override executable paths", () => {
  assert(matchesDockApplication(item("a", ""), window("a", null), []));
  assert(!matchesDockApplication(item("a"), window("b"), []));
});
Deno.test("legacy executable pin adopts a unique running app ID", () => {
  const win = window("a");
  assert(matchesDockApplication(item(null), win, [win]));
});
Deno.test("legacy pin does not merge different browser/PWA identities", () => {
  const windows = [window("a"), window("b", "c:/app.exe", 2)];
  assert(!matchesDockApplication(item(null), windows[0]!, windows));
  assert(!matchesDockApplication(item("a"), window(null), windows));
});
Deno.test("monitor-filtered list must use the global inventory for ambiguity", () => {
  const local = window("a");
  const otherMonitor = window("b", "c:/app.exe", 2);
  assert(!matchesDockApplication(item(null), local, [local, otherMonitor]));
});
Deno.test("relaunch executable supports legacy shortcut pins", () => {
  assert(matchesDockApplication({ ...item(null, "app.lnk"), relaunch: { command: "C:/APP.exe" } }, window(null), []));
});
Deno.test("unidentified window never matches an empty launch path", () => {
  assert(!matchesDockApplication(item(null, ""), window(null, null), []));
});
Deno.test("explicit pin can match a no-ID window without competing identities", () => {
  assert(matchesDockApplication(item("a"), window(null), [window("a"), window(null)]));
});
Deno.test("path keys converge across case and separator differences", () => {
  assert(runningApplicationKey(window(null, "C:/App.exe")) === runningApplicationKey(window(null, "c:\\APP.exe")));
});
Deno.test("no-identity windows get distinct session entries instead of being omitted", () => {
  assert(runningApplicationKey(window(null, null, 1)) !== runningApplicationKey(window(null, null, 2)));
});
Deno.test("session placeholder only matches its live window and process", () => {
  const placeholder = { ...item(null, ""), sessionWindow: { hwnd: 1, pid: 10 } };
  assert(matchesDockApplication(placeholder, window(null, null), []));
  assert(!matchesDockApplication(placeholder, window(null, null, 2), []));
  assert(!matchesDockApplication(placeholder, { ...window(null, null), process: { id: 11, path: null } }, []));
  assert(!canLaunchDockApplication(placeholder));
});
Deno.test("empty saved entry cannot launch but ID-only installed app can", () => {
  assert(!canLaunchDockApplication(item(null, "")));
  assert(canLaunchDockApplication(item("registered!App", "")));
});
