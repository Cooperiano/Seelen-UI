import { orderTaskbarSections } from "./taskbarSections.ts";

export interface TaskbarDropItem {
  id: string;
  type: string;
  pinned?: boolean;
  preventPinning?: boolean;
  sessionWindow?: unknown;
}

export const TASKBAR_BOUNDARY = "suite-application-section-boundary";
export type TaskbarBoundary = { id: typeof TASKBAR_BOUNDARY; type: "Separator" };

export function withTaskbarBoundary<T extends TaskbarDropItem>(items: T[], dragging: boolean): (T | TaskbarBoundary)[] {
  const apps = items.filter(i => i.type === "AppOrFile");
  const pinned = apps.some(i => i.pinned);
  const temporary = apps.some(i => !i.pinned);
  if (!apps.length || (!dragging && !(pinned && temporary))) return items;
  const firstTemporary = items.findIndex(i => i.type === "AppOrFile" && !i.pinned);
  const index = firstTemporary >= 0 ? firstTemporary : items.lastIndexOf(apps[apps.length - 1]!) + 1;
  return [...items.slice(0, index), { id: TASKBAR_BOUNDARY, type: "Separator" }, ...items.slice(index)];
}

export function commitTaskbarDrop<T extends TaskbarDropItem>(
  current: T[], preview: TaskbarDropItem[], sourceId: string, accepted: boolean,
): T[] {
  if (!accepted) return current;
  const source = current.find(i => i.id === sourceId);
  const boundary = preview.findIndex(i => i.id === TASKBAR_BOUNDARY);
  const sourceIndex = preview.findIndex(i => i.id === sourceId);
  if (!source || boundary < 0 || sourceIndex < 0 || new Set(preview.map(i => i.id)).size !== preview.length) return current;

  const byId = new Map(current.map(i => [i.id, i]));
  // Resolve against current objects: a live app may have opened, closed or changed metadata during the drag.
  const ordered = preview.flatMap(i => {
    const item = byId.get(i.id);
    return item ? [item] : [];
  });
  for (let index = 0; index < current.length; index++) {
    const item = current[index]!;
    if (ordered.some(i => i.id === item.id)) continue;
    const following = current.slice(index + 1).find(i => ordered.some(o => o.id === i.id));
    const insertion = following ? ordered.findIndex(i => i.id === following.id) : ordered.length;
    ordered.splice(insertion, 0, item);
  }
  const wantsPin = sourceIndex < boundary;
  const canChangePin = source.type === "AppOrFile" && !source.sessionWindow && (!wantsPin || !source.preventPinning);
  return orderTaskbarSections(ordered.map(i =>
    i.id === sourceId && canChangePin ? { ...i, pinned: wantsPin } : i
  ));
}
