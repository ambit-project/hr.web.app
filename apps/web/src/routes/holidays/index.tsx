import { createFileRoute } from "@tanstack/react-router";
import { z } from "zod";
import { requireRoles } from "#/features/auth/session";
import { HolidaysPage } from "#/features/time-absence/HolidaysPage";

export const Route = createFileRoute("/holidays/")({
	validateSearch: z.object({
		year: z.coerce.number().int().min(2000).max(2100).optional().catch(undefined),
	}),
	beforeLoad: ({ context }) => requireRoles(context.me, "kadry", "administrator"),
	component: HolidaysPage,
});
