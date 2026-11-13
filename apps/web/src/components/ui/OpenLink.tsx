import { ChevronRight } from "lucide-react";

/**
 * Last column of a table whose rows have a details page: a plain ">" that gets a circle in the action colour (black in
 * the light theme, white in the dark one) on hover or focus — see .open-link in base.css. Kept as a class plus icon
 * because the router link's typed `to`/`params` stay at the caller.
 */
export const openLinkClass = "open-link";

export function OpenIcon() {
	return <ChevronRight className="size-4" aria-hidden />;
}
