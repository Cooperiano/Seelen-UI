// Measure V8 blocks in the actual dynamic toolbar template. Deno's normal
// module coverage excludes Function bodies, so it cannot measure this plugin.
import { Session } from "node:inspector";
import { readFileSync } from "node:fs";
import { strict as assert } from "node:assert";

const session = new Session();
session.connect();
const post = (method, params = {}) =>
  new Promise((resolve, reject) => {
    session.post(method, params, (error, result) => error ? reject(error) : resolve(result));
  });
let tests = 0;
globalThis.Deno = {
  readTextFileSync: (path) => readFileSync(path, "utf8"),
  test: (_name, test) => {
    test();
    tests++;
  },
};
try {
  await post("Profiler.enable");
  await post("Profiler.startPreciseCoverage", { callCount: true, detailed: true });
  await import("./suite_tray_test.ts");
  const { result } = await post("Profiler.takePreciseCoverage");
  const blocks = new Map();
  for (const script of result.filter((s) => s.url === "suite-tray-template.js")) {
    for (const fn of script.functions) {
      for (const range of fn.ranges) {
        const key = `${fn.functionName}:${range.startOffset}:${range.endOffset}`;
        blocks.set(key, (blocks.get(key) || 0) + range.count);
      }
    }
  }
  assert.equal(tests, 7);
  assert.ok(blocks.size > 0, "Actual dynamic template was not measured");
  const covered = [...blocks.values()].filter((count) => count > 0).length;
  const percent = covered * 100 / blocks.size;
  console.log(JSON.stringify({ tests, scope: "actual tray template V8 blocks", covered, total: blocks.size, percent }));
  assert.ok(percent >= 80, "Tray template block coverage is below 80%");
} finally {
  await post("Profiler.stopPreciseCoverage");
  session.disconnect();
  delete globalThis.Deno;
}
