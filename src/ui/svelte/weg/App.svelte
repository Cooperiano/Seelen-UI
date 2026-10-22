<script lang="ts">
  import { Widget, invoke, SeelenCommand } from "@seelen-ui/lib";
  import { HideMode, ZOrder } from "@seelen-ui/lib/types";
  import { onMount } from "svelte";
  import { debounce } from "lodash";
  import Dock from "./components/Dock.svelte";
  import { windowsState, focused, widgetStatuses } from "./state/windows.svelte.ts";
  import { settingsState } from "./state/settings.svelte.ts";
  import { dockShouldBeHidden } from "./state/hidden.svelte.ts";
  import { systemState } from "./state/system.svelte.ts";

  const startMenuExes = ["SearchHost.exe", "StartMenuExperienceHost.exe"];

  const topWindowIsFullscreen = $derived(windowsState.topInteractableWindow?.isFullscreen);

  const focusedIsAppsMenu = $derived(
    startMenuExes.some((program) => (focused.value?.exe || "").endsWith(program)) ||
      widgetStatuses.value.some(
        (w) => w.widgetId === "@seelen/apps-menu" && w.webviewWindowId === focused.value?.hwnd,
      ),
  );

  const showWidget = $derived(!topWindowIsFullscreen || focusedIsAppsMenu);
  const setWidgetVisibility = debounce(async (value: boolean, revealEdge: string | null) => {
    try {
      if (value) {
        await Widget.self.show();
        // A shell/foreground transition can cover a still-visible topmost Dock.
        // Reassert its layer only on explicit edge reveal, never by taking focus.
        // Recheck after awaiting so newer fullscreen/edge state wins.
        if (revealEdge !== null && showWidget && settingsState.isReady &&
          systemState.mouseAtEdge === revealEdge && settingsState.position === revealEdge) {
          await invoke(SeelenCommand.SetSelfZOrder, { zOrder: ZOrder.TopMost });
        }
      } else {
        await Widget.self.hide();
      }
    } catch (error) {
      console.error("Failed to update Dock visibility", error);
    }
  }, 100);

  $effect(() => {
    if (settingsState.isReady) {
      // Track edge entry even when native visibility did not change.
      const revealEdge = systemState.mouseAtEdge === settingsState.position ? settingsState.position : null;
      setWidgetVisibility(showWidget, revealEdge);
    }
  });

  onMount(() => {
    Widget.self.ready({ show: false }).then(() => {
      settingsState.isReady = true;
    });
    const pulse = () => {
      const surface = document.querySelector<HTMLElement>(".taskbar");
      const rect = surface?.getBoundingClientRect();
      const style = surface && getComputedStyle(surface);
      const intentionallyHidden = settingsState.hideMode === HideMode.Always && dockShouldBeHidden.value;
      const ready = !!surface && !!rect && !!style && settingsState.isReady && showWidget &&
        style.visibility !== "hidden" && rect.width > 100 && rect.height > 20 &&
        (intentionallyHidden || (!surface.classList.contains("hidden") && Number(style.opacity) > 0.1 &&
          rect.top >= 0 && rect.bottom <= innerHeight + 1)) &&
        !!surface.querySelector(".weg-item");
      void invoke(SeelenCommand.DesktopSuiteTaskbarHeartbeat, { ready }).catch(() => {});
    };
    const timer = setInterval(pulse, 1000);
    return () => clearInterval(timer);
  });
</script>

<Dock />
