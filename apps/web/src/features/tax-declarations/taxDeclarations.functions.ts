import { createServerFn } from "@tanstack/react-start";
import {
	createTaxDeclaration,
	deleteTaxDeclaration,
	endTaxDeclaration,
	listTaxDeclarations,
} from "#/api/endpoints/tax-declarations/tax-declarations";
import type { CreateTaxDeclarationRequest, EndTaxDeclarationRequest } from "#/api/models";
import { toResult } from "#/lib/problem";
import { apiOptions, callApi } from "#/server/api";

export const listTaxDeclarationsFn = createServerFn({ method: "GET" })
	.validator((input: { employeeId: string; onlyActive: boolean }) => input)
	.handler(({ data }) =>
		callApi(() =>
			listTaxDeclarations(data.employeeId, { onlyActive: data.onlyActive }, apiOptions()),
		),
	);

export const createTaxDeclarationFn = createServerFn({ method: "POST" })
	.validator((input: { employeeId: string; request: CreateTaxDeclarationRequest }) => input)
	.handler(({ data }) =>
		toResult(() =>
			callApi(() => createTaxDeclaration(data.employeeId, data.request, apiOptions())),
		),
	);

export const endTaxDeclarationFn = createServerFn({ method: "POST" })
	.validator((input: { id: string; request: EndTaxDeclarationRequest }) => input)
	.handler(({ data }) =>
		toResult(() => callApi(() => endTaxDeclaration(data.id, data.request, apiOptions()))),
	);

export const deleteTaxDeclarationFn = createServerFn({ method: "POST" })
	.validator((input: { id: string; rowVersion: number }) => input)
	.handler(({ data }) =>
		toResult(() =>
			callApi(() => deleteTaxDeclaration(data.id, { rowVersion: data.rowVersion }, apiOptions())),
		),
	);
