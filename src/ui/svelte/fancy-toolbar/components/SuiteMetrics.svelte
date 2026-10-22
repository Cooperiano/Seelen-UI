<script lang="ts">
  import { onMount } from "svelte";
  import { invoke, SeelenCommand } from "@seelen-ui/lib";
  import { formatMetric, orderedMetrics, metricLabels, metricColor, chartPath, type MetricsSnapshot } from "../domain/metrics.ts";
  import { settingsState } from "../state/settings.svelte.ts";
  import { t } from "../i18n/index.ts";
  let snapshot = $state<MetricsSnapshot | null>(null);
  let error = $state(false);
  let opening = $state(false);
  let history = $state<(number | null)[][]>(Array.from({length: 14}, () => []));
  onMount(() => {
    let alive = true;
    let timer: ReturnType<typeof setTimeout>;
    async function poll() {
      try {
        const next = await invoke(SeelenCommand.GetDesktopSuiteMetrics) as unknown as MetricsSnapshot | null;
        if (alive) {
          if (next && next.sequence !== snapshot?.sequence) {
            history = history.map((samples, id) => [...samples.slice(-59), next.values[id] ?? null]);
          } else if (!next) {
            history = history.map(samples => [...samples.slice(-59), null]);
          }
          snapshot = next;
        }
      }
      catch { if (alive) snapshot = null; }
      if (alive) timer = setTimeout(poll, 1000);
    }
    poll();
    return () => { alive = false; clearTimeout(timer); };
  });
  async function open(id: number, event: MouseEvent) {
    if (opening) return;
    opening = true;
    error = false;
    const rect = (event.currentTarget as HTMLElement).getBoundingClientRect();
    const origin = settingsState.widgetRect;
    const scale = window.devicePixelRatio;
    try {
      await invoke(SeelenCommand.OpenDesktopSuiteMetric, { id, rect: [
        origin.left + Math.round(rect.left * scale), origin.top + Math.round(rect.top * scale),
        origin.left + Math.round(rect.right * scale), origin.top + Math.round(rect.bottom * scale),
      ] });
    } catch { error = true; }
    finally { opening = false; }
  }
</script>

{#if snapshot}
<div class="suite-metrics" aria-label="Stats">
    {#each orderedMetrics(snapshot) as id (id)}
      <button data-skin="transparent" class="suite-metric" disabled={opening} onclick={(e) => open(id, e)}
        data-display-style={snapshot.styles[id] ?? 0} style:--metric-color={metricColor(snapshot.colors[id])}
        aria-label={`${metricLabels[id]} ${formatMetric(id, snapshot.values[id])}`}
        title={`${metricLabels[id]} ${formatMetric(id, snapshot.values[id])} · ${$t("suite.metric_details")}`}>
        <span class="suite-metric-label">{metricLabels[id]}</span>
        {#if snapshot.styles[id] === 3 && chartPath(history[id] ?? [], id)}
          <svg class="suite-metric-chart" viewBox="0 0 48 22" aria-hidden="true"><path d={chartPath(history[id] ?? [], id)} /></svg>
        {:else}
          <span class="suite-metric-value">{formatMetric(id, snapshot.values[id])}</span>
        {/if}
      </button>
    {/each}
  {#if error}<span role="alert" class="suite-metric-error" title={$t("suite.stats_unavailable")}>!</span>{/if}
</div>
{/if}
