import { createFileRoute } from "@tanstack/react-router";
import { requireRoles } from "#/features/auth/session";
import { PayslipPrintPage } from "#/features/documents/PayslipPrintPage";

// Pracownik opens only their own payslips; the database answers 403 for anyone else's.
export const Route = createFileRoute("/payslips/$entryId")({
	beforeLoad: ({ context }) => requireRoles(context.me, "kadry", "administrator", "pracownik"),
	component: PayslipPrintPage,
});
