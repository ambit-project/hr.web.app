import { queryOptions, useMutation, useQueryClient } from "@tanstack/react-query";
import type {
	AddEmployeeAddressRequest,
	AddEmployeeBankAccountRequest,
	CloseEmployeeBankAccountRequest,
	CreateEmployeeRequest,
	UpdateEmployeeRequest,
} from "#/api/models";
import { employeesKeys } from "#/api/query-keys";
import { unwrap } from "#/lib/problem";
import {
	addEmployeeAddressFn,
	addEmployeeBankAccountFn,
	closeEmployeeBankAccountFn,
	createEmployeeFn,
	deleteEmployeeFn,
	getEmployeeFn,
	linkEmployeeAccountFn,
	listEmployeeAddressesFn,
	listEmployeeBankAccountsFn,
	listEmployeeReadAuditFn,
	listEmployeesFn,
	unlinkEmployeeAccountFn,
	updateEmployeeFn,
} from "./employees.functions";

export const EMPLOYEES_PAGE_SIZE = 20;
export const READ_AUDIT_PAGE_SIZE = 20;

export type EmployeesSearch = { companyId?: string; search?: string; page?: number };

export function employeesQuery({ companyId, search, page }: EmployeesSearch) {
	const params = { companyId, search, page: page ?? 1, pageSize: EMPLOYEES_PAGE_SIZE };
	return queryOptions({
		queryKey: employeesKeys.list(params),
		queryFn: () => listEmployeesFn({ data: params }),
	});
}

/**
 * Every fetch of the details writes a read-audit entry in the database, so the page does not refetch on focus or
 * remount; it reads again only after a change made here.
 */
export function employeeQuery(id: string) {
	return queryOptions({
		queryKey: employeesKeys.detail(id),
		queryFn: async () => unwrap(await getEmployeeFn({ data: id })),
		staleTime: Number.POSITIVE_INFINITY,
		refetchOnWindowFocus: false,
	});
}

export function employeeAddressesQuery(id: string) {
	return queryOptions({
		queryKey: employeesKeys.addresses(id),
		queryFn: () => listEmployeeAddressesFn({ data: id }),
	});
}

export function employeeBankAccountsQuery(id: string) {
	return queryOptions({
		queryKey: employeesKeys.bankAccounts(id),
		queryFn: () => listEmployeeBankAccountsFn({ data: id }),
	});
}

export function employeeReadAuditQuery(id: string, page: number) {
	const params = { page, pageSize: READ_AUDIT_PAGE_SIZE };
	return queryOptions({
		queryKey: employeesKeys.readAudit(id, params),
		queryFn: () => listEmployeeReadAuditFn({ data: { id, params } }),
	});
}

function useEmployeeMutation<TVariables, TResult>(
	mutationFn: (variables: TVariables) => Promise<TResult>,
) {
	const queryClient = useQueryClient();
	return useMutation({
		mutationFn,
		// Also after a failure: a 409 means the data on screen is stale and must be read again.
		onSettled: () => queryClient.invalidateQueries({ queryKey: employeesKeys.all }),
	});
}

export function useCreateEmployee() {
	return useEmployeeMutation(async (request: CreateEmployeeRequest) =>
		unwrap(await createEmployeeFn({ data: request })),
	);
}

export function useUpdateEmployee() {
	return useEmployeeMutation(async (variables: { id: string; request: UpdateEmployeeRequest }) =>
		unwrap(await updateEmployeeFn({ data: variables })),
	);
}

export function useDeleteEmployee() {
	return useEmployeeMutation(async (id: string) => unwrap(await deleteEmployeeFn({ data: id })));
}

export function useAddEmployeeAddress() {
	return useEmployeeMutation(
		async (variables: { id: string; request: AddEmployeeAddressRequest }) =>
			unwrap(await addEmployeeAddressFn({ data: variables })),
	);
}

export function useAddEmployeeBankAccount() {
	return useEmployeeMutation(
		async (variables: { id: string; request: AddEmployeeBankAccountRequest }) =>
			unwrap(await addEmployeeBankAccountFn({ data: variables })),
	);
}

export function useCloseEmployeeBankAccount() {
	return useEmployeeMutation(
		async (variables: { id: string; request: CloseEmployeeBankAccountRequest }) =>
			unwrap(await closeEmployeeBankAccountFn({ data: variables })),
	);
}

export function useLinkEmployeeAccount() {
	return useEmployeeMutation(async (variables: { id: string; account: string }) =>
		unwrap(
			await linkEmployeeAccountFn({
				data: { id: variables.id, request: { account: variables.account } },
			}),
		),
	);
}

export function useUnlinkEmployeeAccount() {
	return useEmployeeMutation(async (id: string) =>
		unwrap(await unlinkEmployeeAccountFn({ data: id })),
	);
}
