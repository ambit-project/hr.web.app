import { createFileRoute } from "@tanstack/react-router";
import { requireRoles } from "#/features/auth/session";
import { PayslipPage } from "#/features/payroll/PayslipPage";

export const Route = createFileRoute("/payroll-entries/$id")({
	beforeLoad: ({ context }) => requireRoles(context.me, "kadry", "administrator"),
	component: PayslipPage,
});
