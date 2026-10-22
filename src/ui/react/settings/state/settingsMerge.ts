// Rebase only local edits onto acknowledged/external state. Arrays are atomic.
export function rebase<T>(base: T, draft: T, remote: T): T {
  if (JSON.stringify(base) === JSON.stringify(draft)) return structuredClone(remote);
  const object = (v: unknown): v is Record<string, unknown> => v !== null && typeof v === "object" && !Array.isArray(v);
  if (!object(base) || !object(draft) || !object(remote)) return structuredClone(draft);
  const result = structuredClone(remote);
  for (const key of new Set([...Object.keys(base), ...Object.keys(draft)])) {
    if (JSON.stringify(base[key]) === JSON.stringify(draft[key])) continue;
    if (!(key in draft)) delete result[key];
    else result[key] = rebase(base[key], draft[key], remote[key]);
  }
  return result as T;
}
