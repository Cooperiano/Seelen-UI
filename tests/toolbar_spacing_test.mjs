import { readFileSync } from "node:fs";
import { test } from "node:test";
import { strict as assert } from "node:assert";
import "./toolbar_desktop_test.mjs";

for (const name of ["src/static/themes/default/styles/fancy-toolbar.scss", "preview-resources/suite-fit/toolbar.scss"]) {
  const css = readFileSync(new URL(`../${name}`, import.meta.url), "utf8");
  test(`${name}: bound only the app tray, not the clock`, () => {
    assert.match(css, /width:\s*max-content;/, "Right group must be able to grow beyond upstream's half-width track");
    assert.match(css, /max-width:\s*calc\(100vw - clamp\(120px, 20vw, 240px\)\);/);
    assert.match(css, /justify-self:\s*end;/);
    assert.match(css, /\.ft-bar-left\s*\{\s*max-width:\s*clamp\(120px, 20vw, 240px\);/);
    const tray = css.match(/\[data-plugin-id="@seelen\/tb-system-tray"\]\s*\{([^{}]*)/)?.[1];
    assert.ok(tray, "App tray needs an independent width budget");
    assert.match(tray, /flex:\s*0 1 auto;/);
    assert.match(tray, /min-width:\s*0;/);
    assert.match(tray, /max-width:\s*min\(40vw, 480px\);/);
    assert.match(css, /\.ft-bar-right > \.ft-bar-item:not\(\[data-plugin-id="@seelen\/tb-system-tray"\]\)\s*\{\s*flex:\s*0 0 auto;/);
  });
  test(`${name}: excess tray icons stay reachable without shrinking their targets`, () => {
    const content = css.match(/\.ft-bar-item-content\s*\{([^{}]*overflow-x:\s*auto;[^{}]*)/)?.[1];
    assert.ok(content, "Tray content needs its own horizontal scrollport");
    assert.match(content, /overflow-y:\s*hidden;/);
    assert.match(content, /height:\s*100%;/);
    assert.match(content, /scrollbar-width:\s*none;/);
    assert.match(css, /button\s*\{\s*flex:\s*0 0 auto;\s*width:\s*calc\(var\(--config-item-size\) \+ var\(--spacing-xs\)\);/);
    assert.match(css, /\.suite-metrics\s*\{\s*min-width:\s*0;/);
  });
}
