import { createFileRoute } from "@tanstack/react-router";
import { z } from "zod";
import { requireRoles } from "#/features/auth/session";
import { PayComponentTypesPage } from "#/features/pay-components/PayComponentTypesPage";
import { componentTypesQuery } from "#/features/pay-components/payComponentsQueries";

const searchSchema = z.object({
	inactive: z.boolean().optional().catch(undefined),
});

export const Route = createFileRoute("/pay-components/")({
	validateSearch: searchSchema,
	beforeLoad: ({ context }) => requireRoles(context.me, "kadry", "administrator"),
	loaderDeps: ({ search }) => search,
	loader: ({ context, deps }) =>
		context.queryClient.query(componentTypesQuery(deps.inactive ?? false)),
	component: PayComponentTypesPage,
});
