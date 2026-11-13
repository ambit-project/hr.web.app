export type Theme = "light" | "dark";

export const THEME_STORAGE_KEY = "hr-theme";

/**
 * Runs inline in <head> before the first paint, so a dark page never flashes white. Without a stored choice it
 * follows the system. Kept as a string because it must not wait for the app bundle.
 */
export const THEME_INIT_SCRIPT = `(function(){try{var p=localStorage.getItem("${THEME_STORAGE_KEY}");var d=p==="dark"||(p!=="light"&&window.matchMedia("(prefers-color-scheme: dark)").matches);var r=document.documentElement;r.classList.toggle("dark",d);r.setAttribute("data-theme",d?"dark":"light");}catch(e){}})();`;

/** The theme the page shows now (set by the init script or a previous toggle). */
export function currentTheme(): Theme {
	return document.documentElement.classList.contains("dark") ? "dark" : "light";
}

export function nextTheme(theme: Theme): Theme {
	return theme === "dark" ? "light" : "dark";
}

export function applyTheme(theme: Theme) {
	const root = document.documentElement;
	root.classList.toggle("dark", theme === "dark");
	root.setAttribute("data-theme", theme);
	try {
		localStorage.setItem(THEME_STORAGE_KEY, theme);
	} catch {
		// Private mode or blocked storage: the theme still applies for this page.
	}
}
