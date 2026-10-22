<script lang="ts">
  import type { SwItem } from "../types.ts";
  import DraggableItem from "./DraggableItem.svelte";
  import WegItemSwitch from "./WegItemSwitch.svelte";
  import TaskbarBoundary from "./TaskbarBoundary.svelte";
  import { TASKBAR_BOUNDARY } from "../domain/taskbarDrop.ts";

  interface Props {
    id: string;
    items: SwItem[];
    itemIndexById: Map<string, number>;
    dragging: boolean;
  }

  let { id, items, itemIndexById, dragging }: Props = $props();
</script>

<div class="weg-items-{id}">
  {#each items as item (item.id)}
    {#if item.id === TASKBAR_BOUNDARY}
      <TaskbarBoundary index={itemIndexById.get(item.id) ?? 0} active={dragging} />
    {:else}
      <DraggableItem {item} index={itemIndexById.get(item.id) ?? 0}>
        <WegItemSwitch {item} />
      </DraggableItem>
    {/if}
  {/each}
</div>
