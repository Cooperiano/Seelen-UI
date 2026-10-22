import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

test('settings sidebar scrolls on the right without reversing navigation text', () => {
  const css = readFileSync(new URL('../src/ui/react/settings/components/navigation/index.module.css', import.meta.url), 'utf8');
  const body = css.split('  .body {')[1]?.split('  .group {')[0];
  assert.ok(body, 'navigation scroll container exists');
  assert.doesNotMatch(body, /direction:\s*rtl/);
  assert.match(body, /direction:\s*ltr/);
  assert.match(body, /overflow-y:\s*auto/);
  assert.match(body, /scrollbar-gutter:\s*stable/);
  assert.doesNotMatch(body, /padding-left:.*-\s*3px/);
});

test('collapsed sidebar retains enough room for icons beside its right gutter', () => {
  const css = readFileSync(new URL('../src/ui/react/settings/components/navigation/index.module.css', import.meta.url), 'utf8');
  assert.match(css, /\.collapsed\s+\.body\s*\{\s*padding-inline:\s*var\(--spacing-2xs\)/);
  // 44px rail - 2*4px outer padding - 3px gutter - 2*4px item padding >= 20px icon.
  assert.ok(44 - 8 - 3 - 8 >= 20);
});
