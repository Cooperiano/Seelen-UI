<script lang="ts">
  import { createSortable } from "@dnd-kit/svelte/sortable";
  import { TASKBAR_BOUNDARY } from "../domain/taskbarDrop.ts";

  let { index, active }: { index: number; active: boolean } = $props();
  const sortable = createSortable({
    id: TASKBAR_BOUNDARY,
    get index() { return index; },
    get disabled() { return { draggable: true, droppable: !active }; },
  });
</script>

<div
  {@attach sortable.attach}
  class="weg-application-section-divider"
  data-drop-active={active}
  data-drop-target={sortable.isDropTarget}
  aria-hidden="true"
></div>
