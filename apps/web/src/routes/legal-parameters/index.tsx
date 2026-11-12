import { createFileRoute } from "@tanstack/react-router";
import { z } from "zod";
import { requireRoles } from "#/features/auth/session";
import { LegalParametersPage } from "#/features/legal-parameters/LegalParametersPage";
import { legalParametersQuery } from "#/features/legal-parameters/legalParametersQueries";

export const Route = createFileRoute("/legal-parameters/")({
	validateSearch: z.object({
		code: z.string().optional().catch(undefined),
		onDate: z
			.string()
			.regex(/^\d{4}-\d{2}-\d{2}$/)
			.optional()
			.catch(undefined),
		page: z.coerce.number().int().min(1).optional().catch(undefined),
	}),
	beforeLoad: ({ context }) => requireRoles(context.me, "administrator"),
	loaderDeps: ({ search }) => search,
	loader: ({ context, deps }) => context.queryClient.query(legalParametersQuery(deps)),
	component: LegalParametersPage,
});
