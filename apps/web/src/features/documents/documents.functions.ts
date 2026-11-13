import { createServerFn } from "@tanstack/react-start";
import {
	generatePit11,
	getConsentDocument,
	getPayslip,
	getTaxForm,
	getTaxFormXml,
	getTaxSummary,
	getWageCard,
	listEmployeePayslips,
	listEmployeeTaxForms,
	listPit11,
	markTaxFormGiven,
	markTaxFormSent,
} from "#/api/endpoints/documents/documents";
import { listMyPayslips, listMyTaxForms } from "#/api/endpoints/my-documents/my-documents";
import type { MarkTaxFormGivenRequest, MarkTaxFormSentRequest } from "#/api/models";
import { toResult } from "#/lib/problem";
import { apiOptions, callApi } from "#/server/api";

// Payslips and wage card

export const getPayslipFn = createServerFn({ method: "GET" })
	.validator((entryId: string) => entryId)
	.handler(({ data }) => toResult(() => callApi(() => getPayslip(data, apiOptions()))));

/** Payslips of an employee record: `mine` — through the self-service route (Pracownik), else the staff route. */
export const listPayslipsFn = createServerFn({ method: "GET" })
	.validator((input: { employeeId: string; mine: boolean }) => input)
	.handler(({ data }) =>
		toResult(() =>
			callApi(() =>
				data.mine
					? listMyPayslips(data.employeeId, apiOptions())
					: listEmployeePayslips(data.employeeId, apiOptions()),
			),
		),
	);

export const getWageCardFn = createServerFn({ method: "GET" })
	.validator((input: { employeeId: string; year: number }) => input)
	.handler(({ data }) =>
		toResult(() => callApi(() => getWageCard(data.employeeId, data.year, apiOptions()))),
	);

// PIT-11

export const listPit11Fn = createServerFn({ method: "GET" })
	.validator((input: { companyId: string; year: number }) => input)
	.handler(({ data }) =>
		toResult(() => callApi(() => listPit11(data.companyId, data.year, apiOptions()))),
	);

export const generatePit11Fn = createServerFn({ method: "POST" })
	.validator((input: { companyId: string; year: number; employeeId: string | null }) => input)
	.handler(({ data }) =>
		toResult(() =>
			callApi(() =>
				generatePit11(data.companyId, data.year, { employeeId: data.employeeId }, apiOptions()),
			),
		),
	);

export const getTaxFormFn = createServerFn({ method: "GET" })
	.validator((id: string) => id)
	.handler(({ data }) => toResult(() => callApi(() => getTaxForm(data, apiOptions()))));

/** The XML file as text; the browser saves it (the API is not reachable from the browser). */
export const getTaxFormXmlFn = createServerFn({ method: "GET" })
	.validator((id: string) => id)
	.handler(({ data }) =>
		toResult(async () => String(await callApi(() => getTaxFormXml(data, apiOptions())))),
	);

export const listTaxFormsFn = createServerFn({ method: "GET" })
	.validator((input: { employeeId: string; mine: boolean }) => input)
	.handler(({ data }) =>
		toResult(() =>
			callApi(() =>
				data.mine
					? listMyTaxForms(data.employeeId, apiOptions())
					: listEmployeeTaxForms(data.employeeId, apiOptions()),
			),
		),
	);

export const markTaxFormSentFn = createServerFn({ method: "POST" })
	.validator((input: { id: string; request: MarkTaxFormSentRequest }) => input)
	.handler(({ data }) =>
		toResult(() => callApi(() => markTaxFormSent(data.id, data.request, apiOptions()))),
	);

export const markTaxFormGivenFn = createServerFn({ method: "POST" })
	.validator((input: { id: string; request: MarkTaxFormGivenRequest }) => input)
	.handler(({ data }) =>
		toResult(() => callApi(() => markTaxFormGiven(data.id, data.request, apiOptions()))),
	);

// Company summary and the consent document

export const getTaxSummaryFn = createServerFn({ method: "GET" })
	.validator((input: { companyId: string; year: number }) => input)
	.handler(({ data }) =>
		toResult(() => callApi(() => getTaxSummary(data.companyId, data.year, apiOptions()))),
	);

export const getConsentDocumentFn = createServerFn({ method: "GET" })
	.validator((id: string) => id)
	.handler(({ data }) => toResult(() => callApi(() => getConsentDocument(data, apiOptions()))));
