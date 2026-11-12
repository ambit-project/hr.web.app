import { createServerFn } from "@tanstack/react-start";
import { createZusCode, listZusCodes, updateZusCode } from "#/api/endpoints/zus-codes/zus-codes";
import type { CreateZusCodeRequest, ListZusCodesParams, UpdateZusCodeRequest } from "#/api/models";
import { toResult } from "#/lib/problem";
import { apiOptions, callApi } from "#/server/api";

export const listZusCodesFn = createServerFn({ method: "GET" })
	.validator((input: ListZusCodesParams) => input)
	.handler(({ data }) => callApi(() => listZusCodes(data, apiOptions())));

export const createZusCodeFn = createServerFn({ method: "POST" })
	.validator((input: CreateZusCodeRequest) => input)
	.handler(({ data }) => toResult(() => callApi(() => createZusCode(data, apiOptions()))));

export const updateZusCodeFn = createServerFn({ method: "POST" })
	.validator((input: { id: string; request: UpdateZusCodeRequest }) => input)
	.handler(({ data }) =>
		toResult(() => callApi(() => updateZusCode(data.id, data.request, apiOptions()))),
	);
