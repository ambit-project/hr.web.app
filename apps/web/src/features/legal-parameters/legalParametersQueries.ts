import { queryOptions, useMutation, useQueryClient } from "@tanstack/react-query";
import type { AddLegalParameterRequest } from "#/api/models";
import { legalParametersKeys } from "#/api/query-keys";
import { unwrap } from "#/lib/problem";
import { addLegalParameterFn, listLegalParametersFn } from "./legalParameters.functions";

export const LEGAL_PARAMETERS_PAGE_SIZE = 50;

export type LegalParametersSearch = { code?: string; onDate?: string; page?: number };

export function legalParametersQuery({ code, onDate, page }: LegalParametersSearch) {
	const params = { code, onDate, page: page ?? 1, pageSize: LEGAL_PARAMETERS_PAGE_SIZE };
	return queryOptions({
		queryKey: legalParametersKeys.list(params),
		queryFn: () => listLegalParametersFn({ data: params }),
	});
}

export function useAddLegalParameter() {
	const queryClient = useQueryClient();
	return useMutation({
		mutationFn: async (request: AddLegalParameterRequest) =>
			unwrap(await addLegalParameterFn({ data: request })),
		onSuccess: () => queryClient.invalidateQueries({ queryKey: legalParametersKeys.all }),
	});
}
