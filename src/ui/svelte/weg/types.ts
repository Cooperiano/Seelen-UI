import type { WegItem } from "@seelen-ui/lib/types";

export type AppOrFileWegItem = Extract<WegItem, { type: "AppOrFile" }> & {
  /** Session-only fallback for a live window whose executable identity is unavailable. Never persisted or launched. */
  sessionWindow?: { hwnd: number; pid: number };
};
export type SeparatorWegItem = Extract<WegItem, { type: "Separator" }>;
export type MediaWegItem = Extract<WegItem, { type: "Media" }>;
export type PluginWegItem = Extract<WegItem, { type: "Plugin" }>;

/** @alias */
export type SwItem = WegItem;
