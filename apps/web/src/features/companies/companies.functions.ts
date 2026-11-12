import { createServerFn } from "@tanstack/react-start";
import { createCompany, listCompanies, updateCompany } from "#/api/endpoints/companies/companies";
import type { CreateCompanyRequest, ListCompaniesParams, UpdateCompanyRequest } from "#/api/models";
import { toResult } from "#/lib/problem";
import { apiOptions, callApi } from "#/server/api";

export const listCompaniesFn = createServerFn({ method: "GET" })
	.validator((input: ListCompaniesParams) => input)
	.handler(({ data }) => callApi(() => listCompanies(data, apiOptions())));

export const createCompanyFn = createServerFn({ method: "POST" })
	.validator((input: CreateCompanyRequest) => input)
	.handler(({ data }) => toResult(() => callApi(() => createCompany(data, apiOptions()))));

export const updateCompanyFn = createServerFn({ method: "POST" })
	.validator((input: { id: string; request: UpdateCompanyRequest }) => input)
	.handler(({ data }) =>
		toResult(() => callApi(() => updateCompany(data.id, data.request, apiOptions()))),
	);
