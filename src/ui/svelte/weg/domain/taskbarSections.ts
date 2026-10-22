type Item = { type: string; id: string; pinned?: boolean };
export function orderTaskbarSections<T extends Item>(items: T[]): T[] {
  const apps = items.filter(item => item.type === "AppOrFile");
  const ordered = [...apps.filter(item => item.pinned), ...apps.filter(item => !item.pinned)];
  let next = 0;
  // Preserve plugins, media and separators, and preserve order within each region.
  return items.map(item => item.type === "AppOrFile" ? ordered[next++]! : item);
}
export function temporarySectionStart<T extends Item>(items: T[]): string | null {
  if (!items.some(item => item.type === "AppOrFile" && item.pinned)) return null;
  return items.find(item => item.type === "AppOrFile" && !item.pinned)?.id ?? null;
}
