import { createFileRoute } from "@tanstack/react-router";
import { z } from "zod";
import { requireRoles } from "#/features/auth/session";
import { CompaniesPage } from "#/features/companies/CompaniesPage";
import { companiesQuery } from "#/features/companies/companiesQueries";

export const Route = createFileRoute("/companies/")({
	validateSearch: z.object({
		search: z.string().optional().catch(undefined),
		page: z.coerce.number().int().min(1).optional().catch(undefined),
	}),
	beforeLoad: ({ context }) => requireRoles(context.me, "kadry", "administrator"),
	loaderDeps: ({ search }) => search,
	loader: ({ context, deps }) => context.queryClient.query(companiesQuery(deps)),
	component: CompaniesPage,
});
