import { createFileRoute } from "@tanstack/react-router";
import { z } from "zod";
import { requireRoles } from "#/features/auth/session";
import { PpkPage } from "#/features/ppk/PpkPage";
import { PPK_TABS } from "#/features/ppk/types";

export const Route = createFileRoute("/ppk/")({
	validateSearch: z.object({
		companyId: z.uuid().optional().catch(undefined),
		tab: z.enum(PPK_TABS).optional().catch(undefined),
		on: z
			.string()
			.regex(/^\d{4}-\d{2}-\d{2}$/)
			.optional()
			.catch(undefined),
		year: z.coerce.number().int().min(2000).max(2100).optional().catch(undefined),
	}),
	beforeLoad: ({ context }) => requireRoles(context.me, "kadry", "administrator"),
	component: PpkPage,
});
