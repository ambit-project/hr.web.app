import { queryOptions } from "@tanstack/react-query";
import { auditKeys } from "#/api/query-keys";
import { listAuditFn } from "./audit.functions";

export const AUDIT_PAGE_SIZE = 50;

export const AUDIT_ENTITIES = [
	"user",
	"company",
	"legal_parameter",
	"position",
	"zus_code",
	"tax_office",
] as const;
export type AuditEntity = (typeof AUDIT_ENTITIES)[number];

export type AuditSearch = { entity?: AuditEntity; from?: string; to?: string; page?: number };

export function auditQuery({ entity, from, to, page }: AuditSearch) {
	const params = { entity, from, to, page: page ?? 1, pageSize: AUDIT_PAGE_SIZE };
	return queryOptions({
		queryKey: auditKeys.list(params),
		queryFn: () => listAuditFn({ data: params }),
	});
}
