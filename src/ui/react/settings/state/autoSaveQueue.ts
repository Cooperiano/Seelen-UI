export type AutoSaveStatus = "idle" | "pending" | "saving" | "saved" | "error";

// One writer, latest draft wins. A failed write is latched until an edit or retry.
export function createAutoSaveQueue(options: {
  key: () => string;
  save: () => Promise<boolean>;
  status: (value: AutoSaveStatus) => void;
  delay?: number;
}) {
  let timer: ReturnType<typeof setTimeout> | undefined;
  let flight: Promise<boolean> | undefined;
  let observed = options.key();
  let failed = false;
  const clear = () => { clearTimeout(timer); timer = undefined; };
  const flush = (): Promise<boolean> => {
    clear();
    if (flight) return flight;
    if (!options.key()) return Promise.resolve(true);
    failed = false;
    options.status("saving");
    flight = (async () => {
      try {
        while (options.key()) {
          const before = options.key();
          if (!await options.save()) throw new Error("save rejected");
          // A transport must acknowledge or advance its snapshot, never spin.
          if (options.key() === before) throw new Error("save not acknowledged");
        }
        options.status("saved");
        return true;
      } catch {
        failed = true;
        options.status("error");
        return false;
      } finally { flight = undefined; }
    })();
    return flight;
  };
  return {
    flush,
    changed(key: string) {
      if (key === observed) return;
      observed = key;
      clear();
      if (flight) return;
      if (!key) { if (!failed) options.status("saved"); return; }
      failed = false;
      options.status("pending");
      timer = setTimeout(() => { void flush(); }, options.delay ?? 250);
    },
  };
}
