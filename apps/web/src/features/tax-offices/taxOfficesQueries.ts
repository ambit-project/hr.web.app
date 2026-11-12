import { keepPreviousData, queryOptions, useMutation, useQueryClient } from "@tanstack/react-query";
import type { CreateTaxOfficeRequest, UpdateTaxOfficeRequest } from "#/api/models";
import { taxOfficesKeys } from "#/api/query-keys";
import { unwrap } from "#/lib/problem";
import { createTaxOfficeFn, listTaxOfficesFn, updateTaxOfficeFn } from "./taxOffices.functions";

export const TAX_OFFICES_PAGE_SIZE = 50;
const PICKER_SIZE = 20;

export type TaxOfficesSearch = { search?: string; inactive?: boolean; page?: number };

export function taxOfficesQuery({ search, inactive, page }: TaxOfficesSearch) {
	const params = {
		search,
		includeInactive: inactive ?? false,
		page: page ?? 1,
		pageSize: TAX_OFFICES_PAGE_SIZE,
	};
	return queryOptions({
		queryKey: taxOfficesKeys.list(params),
		queryFn: () => listTaxOfficesFn({ data: params }),
	});
}

/** Active offices for a picker; the previous result stays on screen while the next term loads. */
export function taxOfficePickerQuery(search: string) {
	const params = { search: search.trim() || undefined, pageSize: PICKER_SIZE };
	return queryOptions({
		queryKey: taxOfficesKeys.list(params),
		queryFn: () => listTaxOfficesFn({ data: params }),
		placeholderData: keepPreviousData,
	});
}

function useTaxOfficeMutation<TVariables, TResult>(
	mutationFn: (variables: TVariables) => Promise<TResult>,
) {
	const queryClient = useQueryClient();
	return useMutation({
		mutationFn,
		// Also after a failure: a 409 means the list is stale and must be read again.
		onSettled: () => queryClient.invalidateQueries({ queryKey: taxOfficesKeys.all }),
	});
}

export function useCreateTaxOffice() {
	return useTaxOfficeMutation(async (request: CreateTaxOfficeRequest) =>
		unwrap(await createTaxOfficeFn({ data: request })),
	);
}

export function useUpdateTaxOffice() {
	return useTaxOfficeMutation(async (variables: { id: string; request: UpdateTaxOfficeRequest }) =>
		unwrap(await updateTaxOfficeFn({ data: variables })),
	);
}
