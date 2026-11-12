import { createFileRoute } from "@tanstack/react-router";
import { z } from "zod";
import { requireRoles } from "#/features/auth/session";
import { TaxOfficesPage } from "#/features/tax-offices/TaxOfficesPage";
import { taxOfficesQuery } from "#/features/tax-offices/taxOfficesQueries";

export const Route = createFileRoute("/tax-offices/")({
	validateSearch: z.object({
		search: z.string().optional().catch(undefined),
		inactive: z.boolean().optional().catch(undefined),
		page: z.coerce.number().int().min(1).optional().catch(undefined),
	}),
	beforeLoad: ({ context }) => requireRoles(context.me, "kadry", "administrator"),
	loaderDeps: ({ search }) => search,
	loader: ({ context, deps }) => context.queryClient.query(taxOfficesQuery(deps)),
	component: TaxOfficesPage,
});
