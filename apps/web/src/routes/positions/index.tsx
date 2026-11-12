import { createFileRoute } from "@tanstack/react-router";
import { z } from "zod";
import { requireRoles } from "#/features/auth/session";
import { PositionsPage } from "#/features/positions/PositionsPage";
import { positionsQuery } from "#/features/positions/positionsQueries";

const searchSchema = z.object({
	search: z.string().optional().catch(undefined),
	page: z.coerce.number().int().min(1).optional().catch(undefined),
});

export const Route = createFileRoute("/positions/")({
	validateSearch: searchSchema,
	beforeLoad: ({ context }) => requireRoles(context.me, "kadry", "administrator"),
	loaderDeps: ({ search }) => search,
	loader: ({ context, deps }) => context.queryClient.query(positionsQuery(deps)),
	component: PositionsPage,
});
