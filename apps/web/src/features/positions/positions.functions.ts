import { createServerFn } from "@tanstack/react-start";
import { createPosition, listPositions, updatePosition } from "#/api/endpoints/positions/positions";
import type {
	CreatePositionRequest,
	ListPositionsParams,
	UpdatePositionRequest,
} from "#/api/models";
import { toResult } from "#/lib/problem";
import { apiOptions, callApi } from "#/server/api";

export const listPositionsFn = createServerFn({ method: "GET" })
	.validator((input: ListPositionsParams) => input)
	.handler(({ data }) => callApi(() => listPositions(data, apiOptions())));

export const createPositionFn = createServerFn({ method: "POST" })
	.validator((input: CreatePositionRequest) => input)
	.handler(({ data }) => toResult(() => callApi(() => createPosition(data, apiOptions()))));

export const updatePositionFn = createServerFn({ method: "POST" })
	.validator((input: { id: string; request: UpdatePositionRequest }) => input)
	.handler(({ data }) =>
		toResult(() => callApi(() => updatePosition(data.id, data.request, apiOptions()))),
	);
