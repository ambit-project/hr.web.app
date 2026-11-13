import { queryOptions, useMutation, useQueryClient } from "@tanstack/react-query";
import type {
	AddPayrollSettingsRequest,
	CreatePayrollRequest,
	SetPayrollMembersRequest,
	SetPayrollPpkPaidRequest,
} from "#/api/models";
import { payComponentsKeys, payrollKeys } from "#/api/query-keys";
import { ProblemError, unwrap } from "#/lib/problem";
import {
	addPayrollSettingsFn,
	calculatePayrollFn,
	closePayrollFn,
	createPayrollFn,
	deletePayrollFn,
	getPayrollEntryFn,
	getPayrollFn,
	getYearTotalsFn,
	listPayrollEntriesFn,
	listPayrollMembersFn,
	listPayrollSettingsFn,
	listPayrollsFn,
	setPayrollMembersFn,
	setPayrollPpkPaidFn,
	type WireWarning,
} from "./payroll.functions";

export type PayslipWarning = { code: string; details: unknown };

const parseWarnings = (warnings: WireWarning[]): PayslipWarning[] =>
	warnings.map((warning) => ({ code: warning.code, details: JSON.parse(warning.details) }));

/** A rule answer (403, 404, 409) does not change on a second try. */
const retryServerFailures = (failureCount: number, error: unknown) =>
	!(error instanceof ProblemError && error.problem.status < 500) && failureCount < 1;

export function payrollSettingsQuery(companyId: string) {
	return queryOptions({
		queryKey: payrollKeys.settings(companyId),
		queryFn: async () => unwrap(await listPayrollSettingsFn({ data: companyId })),
		retry: retryServerFailures,
	});
}

export function payrollsQuery(companyId: string, year: number) {
	return queryOptions({
		queryKey: payrollKeys.lists(companyId, year),
		queryFn: async () => unwrap(await listPayrollsFn({ data: { companyId, year } })),
		retry: retryServerFailures,
	});
}

export function payrollQuery(id: string) {
	return queryOptions({
		queryKey: payrollKeys.payroll(id),
		queryFn: async () => unwrap(await getPayrollFn({ data: id })),
		retry: retryServerFailures,
	});
}

export function payrollMembersQuery(id: string) {
	return queryOptions({
		queryKey: payrollKeys.members(id),
		queryFn: async () => unwrap(await listPayrollMembersFn({ data: id })),
		retry: retryServerFailures,
	});
}

export function payrollEntriesQuery(id: string) {
	return queryOptions({
		queryKey: payrollKeys.entries(id),
		queryFn: async () =>
			unwrap(await listPayrollEntriesFn({ data: id })).map((entry) => ({
				...entry,
				warnings: parseWarnings(entry.warnings),
			})),
		retry: retryServerFailures,
	});
}

export function payrollEntryQuery(id: string) {
	return queryOptions({
		queryKey: payrollKeys.entry(id),
		queryFn: async () => {
			const entry = unwrap(await getPayrollEntryFn({ data: id }));
			return {
				...entry,
				warnings: parseWarnings(entry.warnings),
				lines: entry.lines.map((line) => ({ ...line, trace: JSON.parse(line.trace) as unknown })),
			};
		},
		retry: retryServerFailures,
	});
}

export function yearTotalsQuery(employeeId: string, year: number) {
	return queryOptions({
		queryKey: payrollKeys.yearTotals(employeeId, year),
		queryFn: async () => unwrap(await getYearTotalsFn({ data: { employeeId, year } })),
		retry: retryServerFailures,
	});
}

function usePayrollMutation<TVariables, TResult>(
	mutationFn: (variables: TVariables) => Promise<TResult>,
) {
	const queryClient = useQueryClient();
	return useMutation({
		mutationFn,
		// Also after a failure: a 409 means the data on screen is stale. Closing a list may pay off a garnishment.
		onSettled: async () => {
			await queryClient.invalidateQueries({ queryKey: payrollKeys.all });
			await queryClient.invalidateQueries({ queryKey: payComponentsKeys.all });
		},
	});
}

export function useAddPayrollSettings() {
	return usePayrollMutation(
		async (variables: { companyId: string; request: AddPayrollSettingsRequest }) =>
			unwrap(await addPayrollSettingsFn({ data: variables })),
	);
}

export function useCreatePayroll() {
	return usePayrollMutation(
		async (variables: { companyId: string; request: CreatePayrollRequest }) =>
			unwrap(await createPayrollFn({ data: variables })),
	);
}

type Versioned = { id: string; rowVersion: number | null };

export function useCalculatePayroll() {
	return usePayrollMutation(async (variables: Versioned) =>
		unwrap(await calculatePayrollFn({ data: variables })),
	);
}

export function useClosePayroll() {
	return usePayrollMutation(async (variables: Versioned) =>
		unwrap(await closePayrollFn({ data: variables })),
	);
}

export function useSetPayrollPpkPaid() {
	return usePayrollMutation(async (variables: { id: string; request: SetPayrollPpkPaidRequest }) =>
		unwrap(await setPayrollPpkPaidFn({ data: variables })),
	);
}

export function useDeletePayroll() {
	const queryClient = useQueryClient();
	return useMutation({
		mutationFn: async (variables: Versioned) => unwrap(await deletePayrollFn({ data: variables })),
		// Only marked stale: refetching the page of the deleted list would answer 404 before the page navigates away.
		onSettled: () =>
			queryClient.invalidateQueries({ queryKey: payrollKeys.all, refetchType: "none" }),
	});
}

export function useSetPayrollMembers() {
	return usePayrollMutation(async (variables: { id: string; request: SetPayrollMembersRequest }) =>
		unwrap(await setPayrollMembersFn({ data: variables })),
	);
}
