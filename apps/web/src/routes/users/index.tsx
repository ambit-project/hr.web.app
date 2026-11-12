import { createFileRoute } from "@tanstack/react-router";
import { z } from "zod";
import { requireRoles } from "#/features/auth/session";
import { UsersPage } from "#/features/users/UsersPage";
import { usersQuery } from "#/features/users/usersQueries";

export const Route = createFileRoute("/users/")({
	validateSearch: z.object({
		search: z.string().optional().catch(undefined),
		page: z.coerce.number().int().min(1).optional().catch(undefined),
	}),
	beforeLoad: ({ context }) => requireRoles(context.me, "administrator"),
	loaderDeps: ({ search }) => search,
	loader: ({ context, deps }) => context.queryClient.query(usersQuery(deps)),
	component: UsersPage,
});
