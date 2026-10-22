export interface DockApplicationIdentity {
  umid: string | null;
  path: string;
  relaunch?: { command: string } | null;
  sessionWindow?: { hwnd: number; pid: number };
}

export interface WindowApplicationIdentity {
  hwnd: number;
  umid: string | null;
  process: { id: number; path: string | null };
}

export function normalizeProgramPath(path: string | null | undefined): string {
  return (path ?? "").toLowerCase().replaceAll("/", "\\");
}

/** Path-only pins can adopt a single app ID, but never merge distinct browser/PWA identities. */
export function matchesDockApplication(
  item: DockApplicationIdentity,
  window: WindowApplicationIdentity,
  inventory: readonly WindowApplicationIdentity[],
): boolean {
  if (item.sessionWindow) {
    // Replace the temporary generic entry once the OS supplies a usable application identity.
    if (window.umid || window.process.path) return false;
    return item.sessionWindow.hwnd === window.hwnd && item.sessionWindow.pid === window.process.id;
  }
  if (item.umid && window.umid) return item.umid === window.umid;
  const path = normalizeProgramPath(window.process.path);
  if (!path || (normalizeProgramPath(item.path) !== path && normalizeProgramPath(item.relaunch?.command) !== path)) {
    return false;
  }
  const identities = new Set(
    inventory.filter((candidate) => normalizeProgramPath(candidate.process.path) === path)
      .map((candidate) => candidate.umid).filter((id): id is string => !!id),
  );
  return item.umid ? [...identities].every((id) => id === item.umid) : identities.size <= 1;
}

export function runningApplicationKey(window: WindowApplicationIdentity): string {
  return window.umid ? `app:${window.umid}` : normalizeProgramPath(window.process.path) ||
    `session:${window.process.id}:${window.hwnd}`;
}

export function canLaunchDockApplication(item: DockApplicationIdentity): boolean {
  return !item.sessionWindow && !!(item.umid || item.relaunch?.command || item.path);
}
