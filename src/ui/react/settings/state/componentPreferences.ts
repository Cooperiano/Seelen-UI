import { computed, signal } from "@preact/signals";
import { invoke, SeelenCommand } from "@seelen-ui/lib";
import { difference, isDirty, type PreferencesData } from "../modules/desktopSuite/preferencesDraft.ts";

export type ComponentId = "stats" | "touchpad";
type EditorId = ComponentId | "tracking";
interface Snapshot { data: PreferencesData; revision: string; enabled: boolean; running: boolean }
interface Editor { snapshot: Snapshot | null; draft: PreferencesData; loading: boolean; error: boolean; saved: boolean }
const empty = (): Editor => ({ snapshot: null, draft: {}, loading: false, error: false, saved: false });
export const componentEditors = signal<Record<EditorId, Editor>>({ stats: empty(), touchpad: empty(), tracking: empty() });
export const componentSavePending = signal(false);
export const componentPreferencesFailure = signal<ComponentId | null>(null);
export const componentPreferencesDirty = computed(() => Object.values(componentEditors.value).some((editor) =>
  editor.snapshot !== null && isDirty(editor.snapshot.data, editor.draft)
));
export const trackingAccelerationChanged = computed(() => {
  const editor = componentEditors.value.tracking;
  return editor.snapshot !== null && editor.snapshot.data.Acceleration !== editor.draft.Acceleration;
});

function update(module: EditorId, patch: Partial<Editor>) {
  componentEditors.value = { ...componentEditors.value, [module]: { ...componentEditors.value[module], ...patch } };
}
export async function loadComponentPreferences(module: EditorId, reload = false) {
  const editor = componentEditors.value[module];
  if (editor.loading || (!reload && editor.snapshot) || (editor.snapshot && isDirty(editor.snapshot.data, editor.draft))) return;
  update(module, { loading: true, error: false });
  try {
    const snapshot = (module === "tracking"
      ? await invoke(SeelenCommand.GetDesktopSuiteTracking)
      : await invoke(SeelenCommand.GetDesktopSuitePreferences, { module })) as unknown as Snapshot;
    update(module, { snapshot, draft: structuredClone(snapshot.data), loading: false, error: false, saved: false });
  } catch { update(module, { loading: false, error: true }); }
}
export function editComponentPreference(module: EditorId, key: string, value: unknown) {
  const editor = componentEditors.value[module];
  if (!editor.snapshot || editor.loading || componentSavePending.value) return;
  if (value === undefined) return;
  update(module, { draft: { ...editor.draft, [key]: JSON.parse(JSON.stringify(value)) }, error: false, saved: false });
}
export function discardComponentPreferences(only?: EditorId) {
  if (componentSavePending.value) return;
  for (const module of ["stats", "touchpad", "tracking"] as const) {
    if (only && module !== only) continue;
    const editor = componentEditors.value[module];
    if (editor.snapshot) update(module, { draft: structuredClone(editor.snapshot.data), error: false, saved: false });
  }
}
export async function saveComponentPreferences(accelerationConfirmed = false): Promise<boolean> {
  if (componentSavePending.value) return false;
  componentSavePending.value = true;
  componentPreferencesFailure.value = null;
  try {
    for (const module of ["stats", "touchpad", "tracking"] as const) {
      const editor = componentEditors.value[module];
      if (!editor.snapshot || !isDirty(editor.snapshot.data, editor.draft)) continue;
      try {
        const snapshot = (module === "tracking"
          ? await invoke(SeelenCommand.SaveDesktopSuiteTracking, {
            expected: editor.snapshot.data, desired: editor.draft, confirmed: accelerationConfirmed,
          })
          : await invoke(SeelenCommand.SaveDesktopSuitePreferences, {
            module, revision: editor.snapshot.revision, patch: difference(editor.snapshot.data, editor.draft),
          })) as unknown as Snapshot;
        update(module, { snapshot, draft: structuredClone(snapshot.data), error: false, saved: true });
      } catch {
        update(module, { error: true, saved: false });
        componentPreferencesFailure.value = module === "tracking" ? "touchpad" : module;
        return false;
      }
    }
    return true;
  } finally { componentSavePending.value = false; }
}
