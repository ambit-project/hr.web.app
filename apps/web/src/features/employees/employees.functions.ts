import { createServerFn } from "@tanstack/react-start";
import {
	addEmployeeAddress,
	addEmployeeBankAccount,
	closeEmployeeBankAccount,
	copyEmployees,
	createEmployee,
	deleteEmployee,
	getEmployee,
	linkEmployeeAccount,
	listEmployeeAddresses,
	listEmployeeBankAccounts,
	listEmployeeReadAudit,
	listEmployees,
	unlinkEmployeeAccount,
	updateEmployee,
} from "#/api/endpoints/employees/employees";
import type {
	AddEmployeeAddressRequest,
	AddEmployeeBankAccountRequest,
	CloseEmployeeBankAccountRequest,
	CopyEmployeesRequest,
	CreateEmployeeRequest,
	LinkEmployeeAccountRequest,
	ListEmployeeReadAuditParams,
	ListEmployeesParams,
	UpdateEmployeeRequest,
} from "#/api/models";
import { toResult } from "#/lib/problem";
import { apiOptions, callApi } from "#/server/api";

export const listEmployeesFn = createServerFn({ method: "GET" })
	.validator((input: ListEmployeesParams) => input)
	.handler(({ data }) => callApi(() => listEmployees(data, apiOptions())));

export const getEmployeeFn = createServerFn({ method: "GET" })
	.validator((id: string) => id)
	.handler(({ data }) => toResult(() => callApi(() => getEmployee(data, apiOptions()))));

export const createEmployeeFn = createServerFn({ method: "POST" })
	.validator((input: CreateEmployeeRequest) => input)
	.handler(({ data }) => toResult(() => callApi(() => createEmployee(data, apiOptions()))));

export const copyEmployeesFn = createServerFn({ method: "POST" })
	.validator((input: CopyEmployeesRequest) => input)
	.handler(({ data }) => toResult(() => callApi(() => copyEmployees(data, apiOptions()))));

export const updateEmployeeFn = createServerFn({ method: "POST" })
	.validator((input: { id: string; request: UpdateEmployeeRequest }) => input)
	.handler(({ data }) =>
		toResult(() => callApi(() => updateEmployee(data.id, data.request, apiOptions()))),
	);

export const deleteEmployeeFn = createServerFn({ method: "POST" })
	.validator((id: string) => id)
	.handler(({ data }) => toResult(() => callApi(() => deleteEmployee(data, apiOptions()))));

export const listEmployeeAddressesFn = createServerFn({ method: "GET" })
	.validator((id: string) => id)
	.handler(({ data }) => callApi(() => listEmployeeAddresses(data, apiOptions())));

export const addEmployeeAddressFn = createServerFn({ method: "POST" })
	.validator((input: { id: string; request: AddEmployeeAddressRequest }) => input)
	.handler(({ data }) =>
		toResult(() => callApi(() => addEmployeeAddress(data.id, data.request, apiOptions()))),
	);

export const listEmployeeBankAccountsFn = createServerFn({ method: "GET" })
	.validator((id: string) => id)
	.handler(({ data }) => callApi(() => listEmployeeBankAccounts(data, apiOptions())));

export const addEmployeeBankAccountFn = createServerFn({ method: "POST" })
	.validator((input: { id: string; request: AddEmployeeBankAccountRequest }) => input)
	.handler(({ data }) =>
		toResult(() => callApi(() => addEmployeeBankAccount(data.id, data.request, apiOptions()))),
	);

export const closeEmployeeBankAccountFn = createServerFn({ method: "POST" })
	.validator((input: { id: string; request: CloseEmployeeBankAccountRequest }) => input)
	.handler(({ data }) =>
		toResult(() => callApi(() => closeEmployeeBankAccount(data.id, data.request, apiOptions()))),
	);

export const linkEmployeeAccountFn = createServerFn({ method: "POST" })
	.validator((input: { id: string; request: LinkEmployeeAccountRequest }) => input)
	.handler(({ data }) =>
		toResult(() => callApi(() => linkEmployeeAccount(data.id, data.request, apiOptions()))),
	);

export const unlinkEmployeeAccountFn = createServerFn({ method: "POST" })
	.validator((id: string) => id)
	.handler(({ data }) => toResult(() => callApi(() => unlinkEmployeeAccount(data, apiOptions()))));

export const listEmployeeReadAuditFn = createServerFn({ method: "GET" })
	.validator((input: { id: string; params: ListEmployeeReadAuditParams }) => input)
	.handler(({ data }) => callApi(() => listEmployeeReadAudit(data.id, data.params, apiOptions())));
