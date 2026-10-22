import {
  type DockApplicationIdentity,
  matchesDockApplication,
  type WindowApplicationIdentity,
} from "./applicationIdentity.ts";

/** Keep saved pins independent from the current live-window inventory. No OS or persistence writes. */
export function reconcileApplications<
  Item extends DockApplicationIdentity & { id: string; pinned: boolean },
  Window extends WindowApplicationIdentity,
>(items: readonly Item[], windows: readonly Window[], sessions: readonly WindowApplicationIdentity[] = []) {
  const inventory = [...windows, ...sessions];
  const itemsToRemove = new Set(
    items.filter((item) => !item.pinned && !inventory.some((window) => matchesDockApplication(item, window, inventory)))
      .map((item) => item.id),
  );
  const remaining = items.filter((item) => !itemsToRemove.has(item.id));
  const uncoveredWindows = windows.filter((window) =>
    !remaining.some((item) => matchesDockApplication(item, window, inventory))
  );
  return { itemsToRemove, uncoveredWindows };
}

/** A pin changes retention, never creates a second icon or moves an existing slot. */
export function mergeApplicationInPlace<Item extends DockApplicationIdentity & { id: string; pinned: boolean }>(
  items: readonly Item[], incoming: Item, inventory: readonly WindowApplicationIdentity[],
): Item[] {
  const candidate = { hwnd: 0, umid: incoming.umid, process: { id: 0, path: incoming.path } };
  const index = items.findIndex(item => matchesDockApplication(item, candidate, [...inventory, candidate]));
  if (index < 0) return [...items, incoming];
  return items.map((item, i) => i === index ? { ...item, pinned: item.pinned || incoming.pinned } : item);
}
