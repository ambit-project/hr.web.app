import { createServerFn } from "@tanstack/react-start";
import { listAudit } from "#/api/endpoints/audit/audit";
import type { ListAuditParams } from "#/api/models";
import { apiOptions, callApi } from "#/server/api";

export const listAuditFn = createServerFn({ method: "GET" })
	.validator((input: ListAuditParams) => input)
	.handler(({ data }) => callApi(() => listAudit(data, apiOptions())));
