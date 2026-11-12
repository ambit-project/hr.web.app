import { createFileRoute } from "@tanstack/react-router";
import { z } from "zod";
import { requireRoles } from "#/features/auth/session";
import { ZUS_CODE_KINDS } from "#/features/zus-codes/types";
import { ZusCodesPage } from "#/features/zus-codes/ZusCodesPage";
import { zusCodesQuery } from "#/features/zus-codes/zusCodesQueries";

export const Route = createFileRoute("/zus-codes/")({
	validateSearch: z.object({
		kind: z.enum(ZUS_CODE_KINDS).optional().catch(undefined),
		search: z.string().optional().catch(undefined),
		onDate: z
			.string()
			.regex(/^\d{4}-\d{2}-\d{2}$/)
			.optional()
			.catch(undefined),
		page: z.coerce.number().int().min(1).optional().catch(undefined),
	}),
	beforeLoad: ({ context }) => requireRoles(context.me, "kadry", "administrator"),
	loaderDeps: ({ search }) => search,
	loader: ({ context, deps }) => context.queryClient.query(zusCodesQuery(deps)),
	component: ZusCodesPage,
});
