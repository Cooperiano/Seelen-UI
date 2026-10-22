type Rect = { left: number; top: number; right: number; bottom: number };
export function dockReservation(rect: Rect, side: string, margin: number, scale: number): Rect {
  const result = { ...rect };
  // The outer (screen-edge) margin is already included in the slab. Do not
  // reserve its duplicate on the workspace side: that creates an empty seam.
  const inset = Math.round(margin * scale);
  switch (side) {
    case "Top": result.bottom -= inset; break;
    case "Bottom": result.top += inset; break;
    case "Left": result.right -= inset; break;
    case "Right": result.left += inset; break;
  }
  return result;
}
