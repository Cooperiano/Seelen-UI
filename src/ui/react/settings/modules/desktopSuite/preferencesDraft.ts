export type JsonValue = string | number | boolean | null | JsonValue[] | { [key: string]: JsonValue };
export type PreferencesData = Record<string, JsonValue>;

export function difference(original: PreferencesData, draft: PreferencesData): PreferencesData {
  return Object.fromEntries(Object.entries(draft).filter(([key, value]) =>
    JSON.stringify(original[key]) !== JSON.stringify(value)
  ));
}

export function isDirty(original: PreferencesData, draft: PreferencesData): boolean {
  return Object.keys(difference(original, draft)).length > 0;
}
