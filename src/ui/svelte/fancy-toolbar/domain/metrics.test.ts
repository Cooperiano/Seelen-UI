import { formatMetric, type MetricsSnapshot, orderedMetrics, metricColor, chartPath } from "./metrics.ts";
declare const Deno: { test(name: string, test: () => void): void };

Deno.test("publisher accent and real graph samples preserve missing-data gaps", () => {
  equal(metricColor(0xff65d5ff), "#65d5ff");
  equal(metricColor(0), undefined);
  equal(chartPath([10, null, 20, 30], 0).split("M").length - 1, 2);
  equal(chartPath([], 0), "");
});
function equal(actual: unknown, expected: unknown) {
  if (JSON.stringify(actual) !== JSON.stringify(expected)) {
    throw new Error(`${JSON.stringify(actual)} != ${JSON.stringify(expected)}`);
  }
}
Deno.test("Stats units match v7 sensor, MHz, MB and byte/sec semantics", () => {
  equal(formatMetric(0, 56), "56%");
  equal(formatMetric(3, 39.5), "40°");
  equal(formatMetric(5, 3500), "3.50G");
  equal(formatMetric(7, 14.2), "14W");
  equal(formatMetric(8, 4096), "4.0G");
  equal(formatMetric(10, 1024 * 1024), "1.0M/s");
  equal(formatMetric(13, 2), "2");
});
Deno.test("unsupported and unavailable readings are never fabricated", () => {
  equal(formatMetric(0, null), "—");
  equal(formatMetric(0, NaN), "—");
  equal(formatMetric(99, 10), "—");
});
Deno.test("publisher visibility and order are preserved", () => {
  const s: MetricsSnapshot = {
    sequence: 2,
    enabledMask: 5,
    order: [2, 0, 2, 99, 1],
    values: [],
    styles: [],
    colors: [],
  };
  equal(orderedMetrics(s), [2, 0]);
  equal(orderedMetrics(null), []);
});
