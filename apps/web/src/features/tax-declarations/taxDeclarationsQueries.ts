import { queryOptions, useMutation, useQueryClient } from "@tanstack/react-query";
import type { CreateTaxDeclarationRequest, EndTaxDeclarationRequest } from "#/api/models";
import { taxDeclarationsKeys } from "#/api/query-keys";
import { unwrap } from "#/lib/problem";
import {
	createTaxDeclarationFn,
	deleteTaxDeclarationFn,
	endTaxDeclarationFn,
	listTaxDeclarationsFn,
} from "./taxDeclarations.functions";

export function taxDeclarationsQuery(employeeId: string, onlyActive = false) {
	return queryOptions({
		queryKey: taxDeclarationsKeys.byEmployee(employeeId, onlyActive),
		queryFn: () => listTaxDeclarationsFn({ data: { employeeId, onlyActive } }),
	});
}

function useTaxDeclarationMutation<TVariables, TResult>(
	mutationFn: (variables: TVariables) => Promise<TResult>,
) {
	const queryClient = useQueryClient();
	return useMutation({
		mutationFn,
		// Also after a failure: a 409 means the list on screen is stale.
		onSettled: () => queryClient.invalidateQueries({ queryKey: taxDeclarationsKeys.all }),
	});
}

export function useCreateTaxDeclaration() {
	return useTaxDeclarationMutation(
		async (variables: { employeeId: string; request: CreateTaxDeclarationRequest }) =>
			unwrap(await createTaxDeclarationFn({ data: variables })),
	);
}

export function useEndTaxDeclaration() {
	return useTaxDeclarationMutation(
		async (variables: { id: string; request: EndTaxDeclarationRequest }) =>
			unwrap(await endTaxDeclarationFn({ data: variables })),
	);
}

export function useDeleteTaxDeclaration() {
	return useTaxDeclarationMutation(async (variables: { id: string; rowVersion: number }) =>
		unwrap(await deleteTaxDeclarationFn({ data: variables })),
	);
}
