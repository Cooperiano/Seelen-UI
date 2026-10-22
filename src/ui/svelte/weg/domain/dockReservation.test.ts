import { dockReservation } from "./dockReservation.ts";
declare const Deno: { test(name: string, test: () => void): void };
function assertEquals(actual: unknown, expected: unknown) {
  if (JSON.stringify(actual) !== JSON.stringify(expected)) throw new Error(`${JSON.stringify(actual)} != ${JSON.stringify(expected)}`);
}

for (const scale of [1, 1.25, 1.5, 2]) {
  for (const side of ["Top", "Bottom", "Left", "Right"] as const) {
    Deno.test(`reserve visible dock only: ${side} at ${scale}`, () => {
      const margin = Math.round(8 * scale);
      const thickness = Math.round(72 * scale);
      const hitbox = { left: -1920, top: 32, right: 0, bottom: 1080 };
      if (side === "Bottom") hitbox.top = hitbox.bottom - thickness;
      if (side === "Top") hitbox.bottom = hitbox.top + thickness;
      if (side === "Left") hitbox.right = hitbox.left + thickness;
      if (side === "Right") hitbox.left = hitbox.right - thickness;
      const expected = { ...hitbox };
      if (side === "Bottom") expected.top += margin;
      if (side === "Top") expected.bottom -= margin;
      if (side === "Left") expected.right -= margin;
      if (side === "Right") expected.left += margin;
      assertEquals(dockReservation(hitbox, side, 8, scale), expected);
      assertEquals(dockReservation(hitbox, side, 0, scale), hitbox);
    });
  }
}
