import { queryOptions, useMutation, useQueryClient } from "@tanstack/react-query";
import type { CreatePositionRequest } from "#/api/models";
import { positionsKeys } from "#/api/query-keys";
import { unwrap } from "#/lib/problem";
import { createPositionFn, listPositionsFn } from "./positions.functions";

export const POSITIONS_PAGE_SIZE = 20;

export type PositionsSearch = { search?: string; page?: number };

export function positionsQuery({ search, page }: PositionsSearch) {
	const params = { search, page: page ?? 1, pageSize: POSITIONS_PAGE_SIZE };
	return queryOptions({
		queryKey: positionsKeys.list(params),
		queryFn: () => listPositionsFn({ data: params }),
	});
}

export function useCreatePosition() {
	const queryClient = useQueryClient();
	return useMutation({
		mutationFn: async (request: CreatePositionRequest) =>
			unwrap(await createPositionFn({ data: request })),
		onSuccess: () => queryClient.invalidateQueries({ queryKey: positionsKeys.all }),
	});
}
