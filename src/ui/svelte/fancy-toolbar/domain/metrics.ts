export interface MetricsSnapshot {
  sequence: number;
  enabledMask: number;
  values: (number | null)[];
  order: number[];
  styles: number[];
  colors: number[];
}
// User-selected metric colors are content, not a hardcoded widget/theme palette.
export function metricColor(argb: number | undefined): string | undefined {
  return argb != null && (argb >>> 24) !== 0 ? `#${(argb & 0xffffff).toString(16).padStart(6, "0")}` : undefined;
}
export function chartPath(samples: (number | null)[], id: number): string {
  const valid = samples.filter((v): v is number => v != null && Number.isFinite(v));
  if (valid.length < 2) return "";
  const percentage = [0, 1, 2, 3, 4, 6, 9, 12].includes(id);
  const max = Math.max(percentage ? 100 : 1, ...valid.map(v => Math.abs(v) * 1.1));
  let path = "";
  let drawing = false;
  samples.forEach((value, i) => {
    if (value == null || !Number.isFinite(value)) { drawing = false; return; }
    const x = 1 + i * 46 / Math.max(1, samples.length - 1);
    const y = 19 - Math.min(1, Math.max(0, value / max)) * 17;
    path += `${drawing ? "L" : "M"}${x.toFixed(1)},${y.toFixed(1)}`;
    drawing = true;
  });
  return path;
}
export const metricLabels = [
  "CPU",
  "GPU",
  "RAM",
  "Sensor",
  "CPU°",
  "CPU Hz",
  "GPU°",
  "GPU W",
  "VRAM",
  "Disk",
  "↓",
  "↑",
  "BAT",
  "BT",
];
export function orderedMetrics(s: MetricsSnapshot | null): number[] {
  return s
    ? [...new Set(s.order.filter((id) => Number.isInteger(id) && id >= 0 && id < 14 && (s.enabledMask & (1 << id))))]
    : [];
}
export function formatMetric(id: number, value: number | null | undefined): string {
  if (value == null || !Number.isFinite(value)) return "—";
  switch (id) {
    case 0:
    case 1:
    case 2:
    case 9:
    case 12:
      return `${Math.round(value)}%`;
    case 3:
    case 4:
    case 6:
      return `${Math.round(value)}°`;
    case 5:
      return `${(value / 1000).toFixed(2)}G`;
    case 7:
      return `${Math.round(value)}W`;
    case 8:
      return value >= 1024 ? `${(value / 1024).toFixed(1)}G` : `${Math.round(value)}M`;
    case 10:
    case 11:
      return value >= 1048576 ? `${(value / 1048576).toFixed(1)}M/s` : `${Math.round(value / 1024)}K/s`;
    case 13:
      return `${Math.round(value)}`;
    default:
      return "—";
  }
}
