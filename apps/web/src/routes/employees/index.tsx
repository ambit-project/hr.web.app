import { createFileRoute } from "@tanstack/react-router";
import { z } from "zod";
import { requireRoles } from "#/features/auth/session";
import { EmployeesPage } from "#/features/employees/EmployeesPage";
import { employeesQuery } from "#/features/employees/employeesQueries";

export const Route = createFileRoute("/employees/")({
	validateSearch: z.object({
		companyId: z.uuid().optional().catch(undefined),
		search: z.string().optional().catch(undefined),
		page: z.coerce.number().int().min(1).optional().catch(undefined),
	}),
	beforeLoad: ({ context }) => requireRoles(context.me, "kadry", "administrator"),
	loaderDeps: ({ search }) => search,
	loader: ({ context, deps }) => context.queryClient.query(employeesQuery(deps)),
	component: EmployeesPage,
});
