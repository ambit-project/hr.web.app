import { createFileRoute } from "@tanstack/react-router";
import { requireRoles } from "#/features/auth/session";
import { ConsentDocumentPage } from "#/features/documents/ConsentDocumentPage";

export const Route = createFileRoute("/consent-documents/$id")({
	beforeLoad: ({ context }) => requireRoles(context.me, "kadry", "administrator"),
	component: ConsentDocumentPage,
});
