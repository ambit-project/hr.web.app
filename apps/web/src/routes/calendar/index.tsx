import { createFileRoute } from "@tanstack/react-router";
import { z } from "zod";
import { requireRoles } from "#/features/auth/session";
import { CalendarPage } from "#/features/time-absence/CalendarPage";

export const Route = createFileRoute("/calendar/")({
	validateSearch: z.object({
		companyId: z.uuid().optional().catch(undefined),
		year: z.coerce.number().int().min(2000).max(2100).optional().catch(undefined),
	}),
	beforeLoad: ({ context }) => requireRoles(context.me, "kadry", "administrator"),
	component: CalendarPage,
});
