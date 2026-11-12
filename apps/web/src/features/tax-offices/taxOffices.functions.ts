import { createServerFn } from "@tanstack/react-start";
import {
	createTaxOffice,
	listTaxOffices,
	updateTaxOffice,
} from "#/api/endpoints/tax-offices/tax-offices";
import type {
	CreateTaxOfficeRequest,
	ListTaxOfficesParams,
	UpdateTaxOfficeRequest,
} from "#/api/models";
import { toResult } from "#/lib/problem";
import { apiOptions, callApi } from "#/server/api";

export const listTaxOfficesFn = createServerFn({ method: "GET" })
	.validator((input: ListTaxOfficesParams) => input)
	.handler(({ data }) => callApi(() => listTaxOffices(data, apiOptions())));

export const createTaxOfficeFn = createServerFn({ method: "POST" })
	.validator((input: CreateTaxOfficeRequest) => input)
	.handler(({ data }) => toResult(() => callApi(() => createTaxOffice(data, apiOptions()))));

export const updateTaxOfficeFn = createServerFn({ method: "POST" })
	.validator((input: { id: string; request: UpdateTaxOfficeRequest }) => input)
	.handler(({ data }) =>
		toResult(() => callApi(() => updateTaxOffice(data.id, data.request, apiOptions()))),
	);
