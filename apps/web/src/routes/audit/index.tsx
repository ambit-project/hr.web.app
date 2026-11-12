import { createFileRoute } from "@tanstack/react-router";
import { z } from "zod";
import { AuditPage } from "#/features/audit/AuditPage";
import { AUDIT_ENTITIES, auditQuery } from "#/features/audit/auditQueries";
import { requireRoles } from "#/features/auth/session";

const day = z
	.string()
	.regex(/^\d{4}-\d{2}-\d{2}$/)
	.optional()
	.catch(undefined);

export const Route = createFileRoute("/audit/")({
	validateSearch: z.object({
		entity: z.enum(AUDIT_ENTITIES).optional().catch(undefined),
		from: day,
		to: day,
		page: z.coerce.number().int().min(1).optional().catch(undefined),
	}),
	beforeLoad: ({ context }) => requireRoles(context.me, "administrator"),
	loaderDeps: ({ search }) => search,
	loader: ({ context, deps }) => context.queryClient.query(auditQuery(deps)),
	component: AuditPage,
});
