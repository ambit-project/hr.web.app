import { createFileRoute } from "@tanstack/react-router";
import { requireRoles } from "#/features/auth/session";
import { EmploymentDetailsPage } from "#/features/employments/EmploymentDetailsPage";
import { employmentQuery } from "#/features/employments/employmentsQueries";

export const Route = createFileRoute("/employments/$id")({
	beforeLoad: ({ context }) => requireRoles(context.me, "kadry", "administrator"),
	loader: ({ context, params }) => context.queryClient.query(employmentQuery(params.id)),
	component: EmploymentDetailsPage,
});
