import { createFileRoute } from "@tanstack/react-router";
import { requireRoles } from "#/features/auth/session";
import { Pit11PrintPage } from "#/features/documents/Pit11PrintPage";

// Pracownik opens only their own forms that were sent or handed over; the database decides.
export const Route = createFileRoute("/tax-forms/$id")({
	beforeLoad: ({ context }) => requireRoles(context.me, "kadry", "administrator", "pracownik"),
	component: Pit11PrintPage,
});
