import { queryOptions, useMutation, useQueryClient } from "@tanstack/react-query";
import type { CreatePositionRequest, UpdatePositionRequest } from "#/api/models";
import { positionsKeys } from "#/api/query-keys";
import { unwrap } from "#/lib/problem";
import { createPositionFn, listPositionsFn, updatePositionFn } from "./positions.functions";

export const POSITIONS_PAGE_SIZE = 20;

export type PositionsSearch = { search?: string; inactive?: boolean; page?: number };

export function positionsQuery({ search, inactive, page }: PositionsSearch) {
	const params = {
		search,
		includeInactive: inactive ?? false,
		page: page ?? 1,
		pageSize: POSITIONS_PAGE_SIZE,
	};
	return queryOptions({
		queryKey: positionsKeys.list(params),
		queryFn: () => listPositionsFn({ data: params }),
	});
}

function usePositionMutation<TVariables, TResult>(
	mutationFn: (variables: TVariables) => Promise<TResult>,
) {
	const queryClient = useQueryClient();
	return useMutation({
		mutationFn,
		// Also after a failure: a 409 means the list is stale and must be read again.
		onSettled: () => queryClient.invalidateQueries({ queryKey: positionsKeys.all }),
	});
}

export function useCreatePosition() {
	return usePositionMutation(async (request: CreatePositionRequest) =>
		unwrap(await createPositionFn({ data: request })),
	);
}

export function useUpdatePosition() {
	return usePositionMutation(async (variables: { id: string; request: UpdatePositionRequest }) =>
		unwrap(await updatePositionFn({ data: variables })),
	);
}
