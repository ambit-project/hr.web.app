import { createServerFn } from "@tanstack/react-start";
import { createPosition, listPositions } from "#/api/endpoints/positions/positions";
import type { CreatePositionRequest, ListPositionsParams } from "#/api/models";
import { toResult } from "#/lib/problem";
import { apiOptions, callApi } from "#/server/api";

export const listPositionsFn = createServerFn({ method: "GET" })
	.validator((input: ListPositionsParams) => input)
	.handler(({ data }) => callApi(() => listPositions(data, apiOptions())));

export const createPositionFn = createServerFn({ method: "POST" })
	.validator((input: CreatePositionRequest) => input)
	.handler(({ data }) => toResult(() => callApi(() => createPosition(data, apiOptions()))));
