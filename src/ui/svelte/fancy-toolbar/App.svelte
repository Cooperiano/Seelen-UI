<script lang="ts">
  import { invoke, SeelenCommand, Widget } from "@seelen-ui/lib";
  import { ZOrder } from "@seelen-ui/lib/types";
  import { onMount } from "svelte";
  import { debounce } from "lodash";
  import Toolbar from "./components/Toolbar.svelte";
  import { windowsState, focused, widgetStatuses } from "./state/windows.svelte.ts";
  import { settingsState } from "./state/settings.svelte.ts";

  const startMenuExes = ["SearchHost.exe", "StartMenuExperienceHost.exe"];

  const topWindowIsFullscreen = $derived(windowsState.topInteractableWindow?.isFullscreen);
  const focusedIsDesktop = $derived(
    ["Progman", "WorkerW"].includes(focused.value?.class ?? "") &&
      /(?:^|[\\/])explorer\.exe$/i.test(focused.value?.exe ?? ""),
  );

  const focusedIsAppsMenu = $derived(
    startMenuExes.some((program) => (focused.value?.exe || "").endsWith(program)) ||
      widgetStatuses.value.some(
        (w) => w.widgetId === "@seelen/apps-menu" && w.webviewWindowId === focused.value?.hwnd,
      ),
  );

  const showWidget = $derived(focusedIsDesktop || !topWindowIsFullscreen || focusedIsAppsMenu);
  const setWidgetVisibility = debounce(async (value: boolean, desktopHwnd: number | null) => {
    try {
      if (value) {
        await Widget.self.show();
        // Show Desktop can cover a still-visible overlay. Reassert only this
        // toolbar's layer; the native transport preserves foreground focus.
        // Recheck after awaiting so a newer fullscreen focus is never covered.
        if (desktopHwnd !== null && focusedIsDesktop && focused.value.hwnd === desktopHwnd) {
          await invoke(SeelenCommand.SetSelfZOrder, { zOrder: ZOrder.TopMost });
        }
      } else {
        await Widget.self.hide();
      }
    } catch (error) {
      console.error("Failed to update toolbar visibility", error);
    }
  }, 100);

  $effect(() => {
    if (settingsState.isReady) {
      // Depend on desktop focus even when showWidget was true before Win+D.
      setWidgetVisibility(showWidget, focusedIsDesktop ? focused.value.hwnd : null);
    }
  });

  onMount(() => {
    Widget.self.ready({ show: false }).then(() => {
      settingsState.isReady = true;
    });
  });
</script>

<Toolbar />
