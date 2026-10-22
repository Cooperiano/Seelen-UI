// Exercise the real Seelen toolbar template, not a parallel tray implementation.
export {};
declare const Deno: {
  readTextFileSync(path: URL): string;
  test(name: string, test: () => void): void;
};
const template = Deno.readTextFileSync(
  new URL("../src/static/widgets/system-tray/toolbar-plugin.js", import.meta.url),
) + "\n//# sourceURL=suite-tray-template.js";
type Icon = {
  registryKey: string;
  isVisible: boolean;
  isPromoted: boolean;
  guid: string | null;
  tooltip: string;
  iconPath: string;
};
type Button = {
  tooltip: string;
  onClick: string;
  onAuxClick?: string;
  onContextMenu?: string;
  content: unknown;
};
function fixture(key: string, promoted = false): Icon {
  return {
    registryKey: key,
    isVisible: true,
    isPromoted: promoted,
    guid: null,
    tooltip: `App ${key}`,
    iconPath: `C:/fixture/${key}.png`,
  };
}
function render(icons: Icon[], suite = true, placement = "right"): Button[] {
  return new Function("trayIcons", "self", "Button", "Image", "Icon", template)(
    icons,
    { desktopSuite: suite, placement },
    (props: Button) => props,
    (props: unknown) => props,
    (props: unknown) => props,
  );
}
function equal(actual: unknown, expected: unknown) {
  if (JSON.stringify(actual) !== JSON.stringify(expected)) {
    throw new Error(`Expected ${JSON.stringify(expected)}, got ${JSON.stringify(actual)}`);
  }
}
const names = (buttons: Button[]) => buttons.map((b) => b.tooltip);

Deno.test("suite startup shows all live icons without promotion or overflow button", () => {
  equal(names(render([fixture("1", true), fixture("2")])), ["App 1", "App 2"]);
});
Deno.test("empty suite tray has no expand button or manual action", () => {
  equal(render([]), []);
});
Deno.test("live add, hide, remove, and reopen are rendered from each native snapshot", () => {
  const a = fixture("1");
  const b = fixture("2");
  equal(names(render([a])), ["App 1"]);
  equal(names(render([a, b])), ["App 1", "App 2"]);
  equal(names(render([a, { ...b, isVisible: false }])), ["App 1"]);
  equal(names(render([b])), ["App 2"]);
  equal(names(render([b, fixture("3")])), ["App 2", "App 3"]);
});
Deno.test("system network, volume, battery are not duplicated; native order is retained", () => {
  const system = ["73", "74", "75"].map((part) => ({
    ...fixture(part),
    guid: `7820ae${part}-23e3-4229-82c1-e41cb67d5b9c`,
  }));
  equal(names(render([fixture("2"), ...system, fixture("1")])), ["App 2", "App 1"]);
  equal(names(render([fixture("2"), fixture("1")], true, "left")), ["App 1", "App 2"]);
});
Deno.test("existing left, middle and context click callbacks still target each live icon", () => {
  const [button] = render([fixture("123", true)]);
  if (!button) throw new Error("Live icon button is missing");
  const calls: unknown[] = [];
  for (const action of [button.onClick, button.onAuxClick, button.onContextMenu]) {
    new Function("invoke", "SeelenCommand", action!)(
      (command: string, args: unknown) => calls.push([command, args]),
      { SendSystemTrayIconAction: "send_system_tray_icon_action" },
    );
  }
  equal(
    calls,
    ["LeftClick", "MiddleClick", "RightClick"].map((action) => [
      "send_system_tray_icon_action",
      { id: "123", action },
    ]),
  );
});
Deno.test("non-suite profile retains original pinned icons and overflow", () => {
  equal(names(render([fixture("1", true), fixture("2")], false)), ["App 1", "System Tray"]);
});
Deno.test("real toolbar passes isolated suite mode into the template self scope", () => {
  const source = Deno.readTextFileSync(
    new URL(
      "../src/ui/svelte/fancy-toolbar/components/Item.svelte",
      import.meta.url,
    ),
  );
  if (!/self:\s*\{\s*placement,\s*desktopSuite:\s*isDesktopSuite\s*\}/.test(source)) {
    throw new Error("Suite tray policy is not wired into the real toolbar template scope");
  }
});
