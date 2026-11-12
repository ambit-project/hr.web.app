import { redirect } from "@tanstack/react-router";
import type { AuthEndpointsMeResponse } from "#/api/models";
import type { Role } from "./auth.functions";

export type Me = AuthEndpointsMeResponse;

export function hasRole(me: Me | null | undefined, ...roles: Role[]) {
	return Boolean(me?.roles.some((role) => roles.includes(role as Role)));
}

/** Route guard: without one of the roles the person goes back to their start page. */
export function requireRoles(me: Me | null | undefined, ...roles: Role[]) {
	if (!hasRole(me, ...roles)) {
		throw redirect({ to: "/" });
	}
}

/**
 * Start page by role: staff work with positions today, employees see their documents. An account without any role
 * has no start page (null) and sees the no-access message instead of bouncing between guards.
 */
export function homePath(me: Me): "/positions" | "/my" | null {
	if (hasRole(me, "kadry", "administrator")) return "/positions";
	if (hasRole(me, "pracownik")) return "/my";
	return null;
}
