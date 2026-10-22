import { readFileSync } from "node:fs";
import { test } from "node:test";
import { strict as assert } from "node:assert";

const sources = [
  "../src/static/themes/default/styles/fancy-toolbar.scss",
  "../preview-resources/suite-fit/toolbar.scss",
];

for (const path of sources) {
  test(`${path}: clock reserves its natural width and edge clearance`, () => {
    // Resource contract only; installed native screenshots verify real layout.
    const css = readFileSync(new URL(path, import.meta.url), "utf8");
    const clock = css.match(/\[data-plugin-id="@seelen\/tb-calendar-popup"\]\s*\{([^}]+)\}/)?.[1];
    assert.ok(clock, "Suite calendar needs a non-shrinking clock rule");
    assert.match(clock, /flex:\s*0 0 auto;/);
    assert.match(clock, /font-variant-numeric:\s*tabular-nums;/);
    assert.match(clock, /margin-inline-end:\s*var\(--spacing-2xs\);/);
    assert.match(css, /grid-template-columns:\s*minmax\(0, 1fr\) auto minmax\(0, max-content\);/);
  });
}
