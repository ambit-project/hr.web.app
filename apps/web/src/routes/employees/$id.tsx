import { createFileRoute } from "@tanstack/react-router";
import { z } from "zod";
import { requireRoles } from "#/features/auth/session";
import { EmployeeDetailsPage } from "#/features/employees/EmployeeDetailsPage";
import { employeeQuery } from "#/features/employees/employeesQueries";
import { EMPLOYEE_TABS } from "#/features/employees/types";

export const Route = createFileRoute("/employees/$id")({
	validateSearch: z.object({
		tab: z.enum(EMPLOYEE_TABS).optional().catch(undefined),
	}),
	beforeLoad: ({ context }) => requireRoles(context.me, "kadry", "administrator"),
	loader: ({ context, params }) => context.queryClient.query(employeeQuery(params.id)),
	component: EmployeeDetailsPage,
});
