import { effect, signal } from "@preact/signals";
import { Modal } from "antd";
import i18n from "../i18n";
import { hasChanges, saveSettings, settings } from "./mod.ts";
import { componentEditors, componentPreferencesDirty, editComponentPreference, saveComponentPreferences, trackingAccelerationChanged } from "./componentPreferences.ts";
import { createAutoSaveQueue, type AutoSaveStatus } from "./autoSaveQueue.ts";
import { isDirty } from "../modules/desktopSuite/preferencesDraft.ts";

export const autoSaveStatus = signal<AutoSaveStatus>("idle");
function key() {
  const components = Object.entries(componentEditors.value).filter(([, e]) => e.snapshot && isDirty(e.snapshot.data, e.draft))
    .map(([id, e]) => [id, e.draft]);
  return hasChanges.value || components.length ? JSON.stringify([hasChanges.value ? settings.value : null, components]) : "";
}
const confirm = (title: string, description: string) => new Promise<boolean>((resolve) => Modal.confirm({
  title: i18n.t(title), content: i18n.t(description),
  okText: i18n.t("suite_autosave.apply"), cancelText: i18n.t("cancel"), autoFocusButton: "cancel",
  onOk: () => resolve(true), onCancel: () => resolve(false),
}));

export const settingsAutoSave = createAutoSaveQueue({
  key, status: (value) => { autoSaveStatus.value = value; },
  async save() {
    if (hasChanges.value && !await saveSettings()) return false;
    if (!componentPreferencesDirty.value) return true;
    let accelerationConfirmed = false;
    if (trackingAccelerationChanged.value) {
      accelerationConfirmed = await confirm("suite_preferences.acceleration_confirm", "suite_preferences.acceleration_warning");
      if (!accelerationConfirmed) {
        const editor = componentEditors.value.tracking;
        editComponentPreference("tracking", "Acceleration", editor.snapshot!.data.Acceleration);
      }
    }
    const touchpad = componentEditors.value.touchpad;
    if (touchpad.snapshot?.running && isDirty(touchpad.snapshot.data, touchpad.draft)) {
      if (!await confirm("suite_autosave.touchpad_confirm", "suite_autosave.touchpad_warning")) return false;
    }
    return await saveComponentPreferences(accelerationConfirmed);
  },
});

let started = false;
export function startSettingsAutoSave() {
  if (started) return;
  started = true;
  effect(() => { settingsAutoSave.changed(key()); });
}
