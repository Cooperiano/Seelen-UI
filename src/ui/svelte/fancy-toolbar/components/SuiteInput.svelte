<script lang="ts">
  import { onMount } from "svelte";
  import { invoke, SeelenCommand } from "@seelen-ui/lib";
  import { triggerWidget } from "./Evaluated/actionEvaluator.ts";
  import type { WidgetId } from "@seelen-ui/lib/types";
  import { t } from "../i18n/index.ts";
  let mode = $state("—");
  onMount(() => {
    let alive = true;
    let timer: ReturnType<typeof setTimeout>;
    async function poll() {
      try { const next = await invoke(SeelenCommand.GetDesktopSuiteInput); if (alive && next) mode = next; }
      catch { if (alive) mode = "—"; }
      // Sequential: never queue or adopt an older focused-target response.
      if (alive) timer = setTimeout(poll, 100);
    }
    poll();
    return () => { alive = false; clearTimeout(timer); };
  });
</script>
<button id="suite-input" class="suite-input" data-skin="transparent" title={$t("suite.input_mode")}
  onclick={() => triggerWidget("@seelen/keyboard-selector" as WidgetId, "suite-input")}>{mode}</button>
