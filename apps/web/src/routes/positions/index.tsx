import { createFileRoute } from "@tanstack/react-router";
import { z } from "zod";
import { PositionsPage } from "#/features/positions/PositionsPage";
import { positionsQuery } from "#/features/positions/positionsQueries";

const searchSchema = z.object({
	search: z.string().optional().catch(undefined),
	page: z.coerce.number().int().min(1).optional().catch(undefined),
});

export const Route = createFileRoute("/positions/")({
	validateSearch: searchSchema,
	loaderDeps: ({ search }) => search,
	loader: ({ context, deps }) => context.queryClient.query(positionsQuery(deps)),
	component: PositionsPage,
});
