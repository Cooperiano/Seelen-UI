import { invoke, SeelenCommand, SeelenEvent, subscribe } from "@seelen-ui/lib";
import { batch, computed, effect, signal } from "@preact/signals";
import { monitors } from "./system";
import { cloneDeep } from "lodash";
import i18n from "../i18n";
import { rebase } from "./settingsMerge.ts";

export const DEFAULT_SETTINGS = await invoke(SeelenCommand.StateGetDefaultSettings);

export const settings = signal(await invoke(SeelenCommand.StateGetSettings, { path: null }));
const initialSettings = signal(JSON.stringify(settings.value));
subscribe(SeelenEvent.StateSettingsChanged, ({ payload }) => {
  batch(() => {
    settings.value = rebase(JSON.parse(initialSettings.value), settings.value, payload);
    initialSettings.value = JSON.stringify(payload);
  });
});

export const language = computed(() => settings.value.language);

export const hasChanges = computed(() => initialSettings.value !== JSON.stringify(settings.value));
export const needRestart = signal(false);

const bundledAppConfigs = await invoke(SeelenCommand.StateGetSettingsByApp);
export const appsConfig = computed(() => [...bundledAppConfigs, ...settings.value.byApp]);

export async function saveSettings(): Promise<boolean> {
  const s = structuredClone(settings.value);

  const referenced = new Set<string>();
  const wallConfig = s.byWidget["@seelen/wallpaper-manager"];
  if (wallConfig?.defaultCollection) referenced.add(wallConfig.defaultCollection);
  Object.values(s.monitorsV3).forEach((m) => {
    if (m.wallpaperCollection) referenced.add(m.wallpaperCollection);
    Object.values(m.byWorkspace ?? {}).forEach((ws) => {
      if (ws.wallpaperCollection) referenced.add(ws.wallpaperCollection);
    });
  });

  const cleaned = {
    ...s,
    wallpaperCollections: s.wallpaperCollections.filter((c) => !c.hidden || referenced.has(c.id)),
  };

  try {
    await invoke(SeelenCommand.StateWriteSettings, {
      settings: cleaned,
    });
    const acknowledged = await invoke(SeelenCommand.StateGetSettings, { path: null });
    batch(() => {
      settings.value = rebase(s, settings.value, acknowledged);
      initialSettings.value = JSON.stringify(acknowledged);
    });
    return true;
  } catch {
    return false;
  }
}

const defaultMonitorConfig = await invoke(SeelenCommand.StateGetDefaultMonitorSettings);
effect(() => {
  const sanitized = structuredClone(settings.peek());
  const baseline = JSON.parse(initialSettings.peek());
  for (const monitor of monitors.value) {
    if (!sanitized.monitorsV3[monitor.id]) {
      sanitized.monitorsV3[monitor.id] = cloneDeep(defaultMonitorConfig);
      baseline.monitorsV3[monitor.id] = cloneDeep(defaultMonitorConfig);
    }
  }
  batch(() => {
    settings.value = sanitized;
    initialSettings.value = JSON.stringify(baseline);
  });
});

effect(() => {
  i18n.changeLanguage(language.value);
});

export function restoreToLastSaved() {
  settings.value = JSON.parse(initialSettings.value);
}
