import { ChevronRight } from "lucide-react";

/**
 * Last column of a table whose rows have a details page: a ">" square on the action colour (black in the light theme,
 * white in the dark one). Kept as a class plus icon because the router link's typed `to`/`params` stay at the caller.
 */
export const openLinkClass =
	"inline-flex size-8 items-center justify-center bg-accent text-accent-foreground hover:bg-accent-hover focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus";

export function OpenIcon() {
	return <ChevronRight className="size-4" aria-hidden />;
}
