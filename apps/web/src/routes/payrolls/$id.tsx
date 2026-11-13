import { createFileRoute } from "@tanstack/react-router";
import { requireRoles } from "#/features/auth/session";
import { PayrollPage } from "#/features/payroll/PayrollPage";

export const Route = createFileRoute("/payrolls/$id")({
	beforeLoad: ({ context }) => requireRoles(context.me, "kadry", "administrator"),
	component: PayrollPage,
});
