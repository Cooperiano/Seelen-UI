import { readFileSync } from "node:fs";
import { test } from "node:test";
import { strict as assert } from "node:assert";

const sourceUrl = new URL("../src/static/themes/default/styles/quick-settings.scss", import.meta.url);
const themeUrl = new URL("../preview-resources/suite-fit/", import.meta.url);
const source = readFileSync(sourceUrl, "utf8");
function rootDeclarations(css) {
  const clean = css.replace(/\/\*[\s\S]*?\*\//g, "").replace(/\/\/[^\n]*/g, "");
  const root = clean.match(/\.quick-settings\s*\{([^{}]*)/);
  assert.ok(root, "Actual quick-settings root declaration is missing");
  return Object.fromEntries([...root[1].matchAll(/([\w-]+)\s*:\s*([^;]+);/g)].map((m) => [m[1], m[2].trim()]));
}
function autosize(css, initialViewport, dpi) {
  // Model the existing border-box popup + 10px outer shadow margins and
  // ResizeObserver's ceil(root.scrollWidth * DPR), using actual CSS values.
  const rule = rootDeclarations(css);
  const preferred = Number.parseFloat(rule.width);
  let viewport = initialViewport;
  for (let frame = 0; frame < 128; frame++) {
    let max = Infinity;
    if (rule["max-width"]?.includes("100vw")) {
      assert.equal(rule["max-width"], "calc(100vw - var(--spacing-xl))");
      max = Math.max(0, viewport - 24);
    } else {
      assert.ok(!rule["max-width"] || rule["max-width"] === "none");
    }
    const min = Number.parseFloat(rule["min-width"]) || 0;
    const next = Math.ceil((Math.max(16, min, Math.min(preferred, max)) + 20) * dpi) / dpi;
    if (next === viewport) return next;
    viewport = next;
  }
  throw new Error("Popup size did not converge");
}
test("autosized quick-settings width must not depend on its own viewport", () => {
  const rule = rootDeclarations(source);
  assert.doesNotMatch(rule["max-width"] || "", /(?:s|l|d)?vw|%/);
  assert.equal(rule.width, "340px");
});
test("fresh, collapsed and reopened frames converge to full width at common DPI", () => {
  for (const seed of [1, 20, 36, 200, 360, 800]) {
    for (const dpi of [1, 1.25, 1.5, 2]) {
      assert.equal(autosize(source, seed, dpi), 360, `initial=${seed}, DPR=${dpi}`);
    }
  }
});
test("installed theme patch targets only this popup and matches source dimensions", () => {
  const metadata = readFileSync(new URL("metadata.yml", themeUrl), "utf8");
  assert.match(metadata, /"@seelen\/fancy-toolbar": !include toolbar\.scss/);
  assert.match(metadata, /"@seelen\/quick-settings": !include quick-settings\.scss/);
  const patch = readFileSync(new URL("quick-settings.scss", themeUrl), "utf8");
  const rule = rootDeclarations(patch);
  const original = rootDeclarations(source);
  for (const name of ["width", "min-width", "max-width"]) assert.equal(rule[name], original[name]);
  assert.equal(rule["max-width"], "none");
  assert.equal(autosize(patch, 36, 1.25), 360);
});
