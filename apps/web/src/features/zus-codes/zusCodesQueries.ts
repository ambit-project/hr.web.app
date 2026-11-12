import { queryOptions, useMutation, useQueryClient } from "@tanstack/react-query";
import type { CreateZusCodeRequest, UpdateZusCodeRequest } from "#/api/models";
import { zusCodesKeys } from "#/api/query-keys";
import { unwrap } from "#/lib/problem";
import type { ZusCodeKind } from "./types";
import { createZusCodeFn, listZusCodesFn, updateZusCodeFn } from "./zusCodes.functions";

export const ZUS_CODES_PAGE_SIZE = 50;

export type ZusCodesSearch = {
	kind?: ZusCodeKind;
	search?: string;
	onDate?: string;
	page?: number;
};

export function zusCodesQuery({ kind, search, onDate, page }: ZusCodesSearch) {
	const params = { kind, search, onDate, page: page ?? 1, pageSize: ZUS_CODES_PAGE_SIZE };
	return queryOptions({
		queryKey: zusCodesKeys.list(params),
		queryFn: () => listZusCodesFn({ data: params }),
	});
}

function useZusCodeMutation<TVariables, TResult>(
	mutationFn: (variables: TVariables) => Promise<TResult>,
) {
	const queryClient = useQueryClient();
	return useMutation({
		mutationFn,
		// Also after a failure: a 409 means the list is stale and must be read again.
		onSettled: () => queryClient.invalidateQueries({ queryKey: zusCodesKeys.all }),
	});
}

export function useCreateZusCode() {
	return useZusCodeMutation(async (request: CreateZusCodeRequest) =>
		unwrap(await createZusCodeFn({ data: request })),
	);
}

export function useUpdateZusCode() {
	return useZusCodeMutation(async (variables: { id: string; request: UpdateZusCodeRequest }) =>
		unwrap(await updateZusCodeFn({ data: variables })),
	);
}
