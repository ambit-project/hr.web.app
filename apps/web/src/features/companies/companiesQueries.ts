import { queryOptions, useMutation, useQueryClient } from "@tanstack/react-query";
import type { CreateCompanyRequest, UpdateCompanyRequest } from "#/api/models";
import { companiesKeys } from "#/api/query-keys";
import { unwrap } from "#/lib/problem";
import { createCompanyFn, listCompaniesFn, updateCompanyFn } from "./companies.functions";

export const COMPANIES_PAGE_SIZE = 20;

export type CompaniesSearch = { search?: string; page?: number };

export function companiesQuery({ search, page }: CompaniesSearch) {
	const params = { search, page: page ?? 1, pageSize: COMPANIES_PAGE_SIZE, includeInactive: true };
	return queryOptions({
		queryKey: companiesKeys.list(params),
		queryFn: () => listCompaniesFn({ data: params }),
	});
}

function useCompanyMutation<TVariables, TResult>(
	mutationFn: (variables: TVariables) => Promise<TResult>,
) {
	const queryClient = useQueryClient();
	return useMutation({
		mutationFn,
		// Also after a failure: a 409 means the list is stale and must be read again.
		onSettled: () => queryClient.invalidateQueries({ queryKey: companiesKeys.all }),
	});
}

export function useCreateCompany() {
	return useCompanyMutation(async (request: CreateCompanyRequest) =>
		unwrap(await createCompanyFn({ data: request })),
	);
}

export function useUpdateCompany() {
	return useCompanyMutation(async (variables: { id: string; request: UpdateCompanyRequest }) =>
		unwrap(await updateCompanyFn({ data: variables })),
	);
}
