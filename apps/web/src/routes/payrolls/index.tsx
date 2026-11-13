import { createFileRoute } from "@tanstack/react-router";
import { z } from "zod";
import { requireRoles } from "#/features/auth/session";
import { PayrollsPage } from "#/features/payroll/PayrollsPage";
import { PAYROLL_TABS } from "#/features/payroll/types";

export const Route = createFileRoute("/payrolls/")({
	validateSearch: z.object({
		companyId: z.uuid().optional().catch(undefined),
		year: z.coerce.number().int().min(2000).max(2100).optional().catch(undefined),
		tab: z.enum(PAYROLL_TABS).optional().catch(undefined),
	}),
	beforeLoad: ({ context }) => requireRoles(context.me, "kadry", "administrator"),
	component: PayrollsPage,
});
