import { createFileRoute } from "@tanstack/react-router";
import { z } from "zod";
import { requireRoles } from "#/features/auth/session";
import { EmploymentDetailsPage } from "#/features/employments/EmploymentDetailsPage";
import { employmentQuery } from "#/features/employments/employmentsQueries";

export const Route = createFileRoute("/employments/$id")({
	// Year of the "Czas pracy" sections, so a link or a refresh keeps it.
	validateSearch: z.object({
		year: z.coerce.number().int().min(2000).max(2100).optional().catch(undefined),
	}),
	beforeLoad: ({ context }) => requireRoles(context.me, "kadry", "administrator"),
	loader: ({ context, params }) => context.queryClient.query(employmentQuery(params.id)),
	component: EmploymentDetailsPage,
});
