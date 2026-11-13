import { queryOptions, useMutation, useQueryClient } from "@tanstack/react-query";
import type { MarkTaxFormGivenRequest, MarkTaxFormSentRequest } from "#/api/models";
import { documentsKeys } from "#/api/query-keys";
import { ProblemError, unwrap } from "#/lib/problem";
import {
	generatePit11Fn,
	getConsentDocumentFn,
	getPayslipFn,
	getTaxFormFn,
	getTaxFormXmlFn,
	getTaxSummaryFn,
	getWageCardFn,
	listPayslipsFn,
	listPit11Fn,
	listTaxFormsFn,
	markTaxFormGivenFn,
	markTaxFormSentFn,
} from "./documents.functions";

/** A rule answer (403, 404, 409) does not change on a second try. */
const retryServerFailures = (failureCount: number, error: unknown) =>
	!(error instanceof ProblemError && error.problem.status < 500) && failureCount < 1;

export function payslipQuery(entryId: string) {
	return queryOptions({
		queryKey: documentsKeys.payslip(entryId),
		queryFn: async () => unwrap(await getPayslipFn({ data: entryId })),
		retry: retryServerFailures,
	});
}

export function payslipsQuery(employeeId: string, mine: boolean) {
	return queryOptions({
		queryKey: [...documentsKeys.payslips(employeeId), { mine }],
		queryFn: async () => unwrap(await listPayslipsFn({ data: { employeeId, mine } })),
		retry: retryServerFailures,
	});
}

export function wageCardQuery(employeeId: string, year: number) {
	return queryOptions({
		queryKey: documentsKeys.wageCard(employeeId, year),
		queryFn: async () => unwrap(await getWageCardFn({ data: { employeeId, year } })),
		retry: retryServerFailures,
	});
}

export function pit11Query(companyId: string, year: number) {
	return queryOptions({
		queryKey: documentsKeys.pit11(companyId, year),
		queryFn: async () => unwrap(await listPit11Fn({ data: { companyId, year } })),
		retry: retryServerFailures,
	});
}

export function taxFormQuery(id: string) {
	return queryOptions({
		queryKey: documentsKeys.taxForm(id),
		queryFn: async () => unwrap(await getTaxFormFn({ data: id })),
		retry: retryServerFailures,
	});
}

export function taxFormsQuery(employeeId: string, mine: boolean) {
	return queryOptions({
		queryKey: [...documentsKeys.taxForms(employeeId), { mine }],
		queryFn: async () => unwrap(await listTaxFormsFn({ data: { employeeId, mine } })),
		retry: retryServerFailures,
	});
}

export function taxSummaryQuery(companyId: string, year: number) {
	return queryOptions({
		queryKey: documentsKeys.taxSummary(companyId, year),
		queryFn: async () => unwrap(await getTaxSummaryFn({ data: { companyId, year } })),
		retry: retryServerFailures,
	});
}

export function consentDocumentQuery(id: string) {
	return queryOptions({
		queryKey: documentsKeys.consentDocument(id),
		queryFn: async () => unwrap(await getConsentDocumentFn({ data: id })),
		retry: retryServerFailures,
	});
}

/** The XML of a form as text (not cached: each download reads the current file). */
export async function fetchTaxFormXml(id: string): Promise<string> {
	return unwrap(await getTaxFormXmlFn({ data: id }));
}

function useDocumentsMutation<TVariables, TResult>(
	mutationFn: (variables: TVariables) => Promise<TResult>,
) {
	const queryClient = useQueryClient();
	return useMutation({
		mutationFn,
		// Also after a failure: a 409 (already sent, not the latest version) means the screen is stale.
		onSettled: () => queryClient.invalidateQueries({ queryKey: documentsKeys.all }),
	});
}

export function useGeneratePit11() {
	return useDocumentsMutation(
		async (variables: { companyId: string; year: number; employeeId: string | null }) =>
			unwrap(await generatePit11Fn({ data: variables })),
	);
}

export function useMarkTaxFormSent() {
	return useDocumentsMutation(async (variables: { id: string; request: MarkTaxFormSentRequest }) =>
		unwrap(await markTaxFormSentFn({ data: variables })),
	);
}

export function useMarkTaxFormGiven() {
	return useDocumentsMutation(async (variables: { id: string; request: MarkTaxFormGivenRequest }) =>
		unwrap(await markTaxFormGivenFn({ data: variables })),
	);
}
