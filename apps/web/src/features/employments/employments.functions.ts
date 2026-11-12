import { createServerFn } from "@tanstack/react-start";
import {
	addEmploymentTerms,
	createEmployment,
	deleteEmployment,
	deleteLastEmploymentTerms,
	getEmployment,
	listEmployments,
	terminateEmployment,
	updateEmployment,
} from "#/api/endpoints/employments/employments";
import type {
	AddEmploymentTermsRequest,
	CreateEmploymentRequest,
	TerminateEmploymentRequest,
	UpdateEmploymentRequest,
} from "#/api/models";
import { toResult } from "#/lib/problem";
import { apiOptions, callApi } from "#/server/api";

export const listEmploymentsFn = createServerFn({ method: "GET" })
	.validator((employeeId: string) => employeeId)
	.handler(({ data }) => callApi(() => listEmployments(data, apiOptions())));

export const getEmploymentFn = createServerFn({ method: "GET" })
	.validator((id: string) => id)
	.handler(({ data }) => toResult(() => callApi(() => getEmployment(data, apiOptions()))));

export const createEmploymentFn = createServerFn({ method: "POST" })
	.validator((input: { employeeId: string; request: CreateEmploymentRequest }) => input)
	.handler(({ data }) =>
		toResult(() => callApi(() => createEmployment(data.employeeId, data.request, apiOptions()))),
	);

export const updateEmploymentFn = createServerFn({ method: "POST" })
	.validator((input: { id: string; request: UpdateEmploymentRequest }) => input)
	.handler(({ data }) =>
		toResult(() => callApi(() => updateEmployment(data.id, data.request, apiOptions()))),
	);

export const terminateEmploymentFn = createServerFn({ method: "POST" })
	.validator((input: { id: string; request: TerminateEmploymentRequest }) => input)
	.handler(({ data }) =>
		toResult(() => callApi(() => terminateEmployment(data.id, data.request, apiOptions()))),
	);

export const deleteEmploymentFn = createServerFn({ method: "POST" })
	.validator((id: string) => id)
	.handler(({ data }) => toResult(() => callApi(() => deleteEmployment(data, apiOptions()))));

export const addEmploymentTermsFn = createServerFn({ method: "POST" })
	.validator((input: { id: string; request: AddEmploymentTermsRequest }) => input)
	.handler(({ data }) =>
		toResult(() => callApi(() => addEmploymentTerms(data.id, data.request, apiOptions()))),
	);

export const deleteLastEmploymentTermsFn = createServerFn({ method: "POST" })
	.validator((id: string) => id)
	.handler(({ data }) =>
		toResult(() => callApi(() => deleteLastEmploymentTerms(data, apiOptions()))),
	);
