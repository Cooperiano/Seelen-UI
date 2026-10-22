import { getName } from "@tauri-apps/api/app";

// The backend refuses a desktop-suite build paired with any other app-data identity.
export const suiteDevelopmentBuild = (await getName()) === "Stats Desktop";
