export const THEME_PREFERENCES = ["light", "dark", "system"] as const;
export type ThemePreference = (typeof THEME_PREFERENCES)[number];

export const THEME_STORAGE_KEY = "hr-theme";

/**
 * Runs inline in <head> before the first paint, so a dark page never flashes white. Kept as a string because it
 * must not wait for the app bundle; `applyTheme` below does the same at runtime.
 */
export const THEME_INIT_SCRIPT = `(function(){try{var p=localStorage.getItem("${THEME_STORAGE_KEY}");var d=p==="dark"||((p===null||p==="system")&&window.matchMedia("(prefers-color-scheme: dark)").matches);var r=document.documentElement;r.classList.toggle("dark",d);r.setAttribute("data-theme",d?"dark":"light");}catch(e){}})();`;

export function readPreference(): ThemePreference {
	try {
		const value = localStorage.getItem(THEME_STORAGE_KEY);
		return THEME_PREFERENCES.includes(value as ThemePreference)
			? (value as ThemePreference)
			: "system";
	} catch {
		return "system";
	}
}

export function resolveDark(preference: ThemePreference, systemDark: boolean): boolean {
	return preference === "dark" || (preference === "system" && systemDark);
}

export function applyTheme(preference: ThemePreference) {
	const dark = resolveDark(preference, window.matchMedia("(prefers-color-scheme: dark)").matches);
	const root = document.documentElement;
	root.classList.toggle("dark", dark);
	root.setAttribute("data-theme", dark ? "dark" : "light");
	try {
		localStorage.setItem(THEME_STORAGE_KEY, preference);
	} catch {
		// Private mode or blocked storage: the theme still applies for this page.
	}
}
