import { queryOptions, useMutation, useQueryClient } from "@tanstack/react-query";
import type {
	AddEmploymentTermsRequest,
	CreateEmploymentRequest,
	TerminateEmploymentRequest,
	UpdateEmploymentRequest,
} from "#/api/models";
import { employeesKeys, employmentsKeys } from "#/api/query-keys";
import { unwrap } from "#/lib/problem";
import {
	addEmploymentTermsFn,
	createEmploymentFn,
	deleteEmploymentFn,
	deleteLastEmploymentTermsFn,
	getEmploymentFn,
	listEmploymentsFn,
	terminateEmploymentFn,
	updateEmploymentFn,
} from "./employments.functions";

export function employmentsQuery(employeeId: string) {
	return queryOptions({
		queryKey: employmentsKeys.byEmployee(employeeId),
		queryFn: () => listEmploymentsFn({ data: employeeId }),
	});
}

/** Reading the terms (with pay) writes a read-audit entry, so the page does not refetch on focus or remount. */
export function employmentQuery(id: string) {
	return queryOptions({
		queryKey: employmentsKeys.detail(id),
		queryFn: async () => unwrap(await getEmploymentFn({ data: id })),
		staleTime: Number.POSITIVE_INFINITY,
		refetchOnWindowFocus: false,
	});
}

function useEmploymentMutation<TVariables, TResult>(
	mutationFn: (variables: TVariables) => Promise<TResult>,
) {
	const queryClient = useQueryClient();
	return useMutation({
		mutationFn,
		// Contracts drive the employee's status and retention date, so both areas are read again — also after a
		// failure, because a 409 means the data on screen is stale.
		onSettled: () =>
			Promise.all([
				queryClient.invalidateQueries({ queryKey: employmentsKeys.all }),
				queryClient.invalidateQueries({ queryKey: employeesKeys.all }),
			]),
	});
}

export function useCreateEmployment() {
	return useEmploymentMutation(
		async (variables: { employeeId: string; request: CreateEmploymentRequest }) =>
			unwrap(await createEmploymentFn({ data: variables })),
	);
}

export function useUpdateEmployment() {
	return useEmploymentMutation(
		async (variables: { id: string; request: UpdateEmploymentRequest }) =>
			unwrap(await updateEmploymentFn({ data: variables })),
	);
}

export function useTerminateEmployment() {
	return useEmploymentMutation(
		async (variables: { id: string; request: TerminateEmploymentRequest }) =>
			unwrap(await terminateEmploymentFn({ data: variables })),
	);
}

export function useDeleteEmployment() {
	return useEmploymentMutation(async (id: string) =>
		unwrap(await deleteEmploymentFn({ data: id })),
	);
}

export function useAddEmploymentTerms() {
	return useEmploymentMutation(
		async (variables: { id: string; request: AddEmploymentTermsRequest }) =>
			unwrap(await addEmploymentTermsFn({ data: variables })),
	);
}

export function useDeleteLastEmploymentTerms() {
	return useEmploymentMutation(async (id: string) =>
		unwrap(await deleteLastEmploymentTermsFn({ data: id })),
	);
}
