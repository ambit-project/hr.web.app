import { createServerFn } from "@tanstack/react-start";
import {
	addPayrollSettings,
	calculatePayroll,
	closePayroll,
	createPayroll,
	deletePayroll,
	getPayroll,
	getPayrollEntry,
	getPayrollYearTotals,
	listPayrollEntries,
	listPayrollMembers,
	listPayrollSettings,
	listPayrolls,
	setPayrollMembers,
	setPayrollPpkPaid,
} from "#/api/endpoints/payroll/payroll";
import type {
	AddPayrollSettingsRequest,
	CreatePayrollRequest,
	GetPayrollEntryLine,
	GetPayrollEntryResponse,
	ListPayrollEntriesItem,
	PayrollWarning,
	SetPayrollMembersRequest,
	SetPayrollPpkPaidRequest,
} from "#/api/models";
import { toResult } from "#/lib/problem";
import { apiOptions, callApi } from "#/server/api";

// Traces and warning details are free JSON objects, which a server function cannot prove serializable; they travel
// as JSON text and are parsed back in the browser (payrollQueries.ts).
export type WireWarning = Omit<PayrollWarning, "details"> & { details: string };
export type WireLine = Omit<GetPayrollEntryLine, "trace"> & { trace: string };
export type WireEntry = Omit<GetPayrollEntryResponse, "warnings" | "lines"> & {
	warnings: WireWarning[];
	lines: WireLine[];
};
export type WireEntryItem = Omit<ListPayrollEntriesItem, "warnings"> & { warnings: WireWarning[] };

const wireWarnings = (warnings: PayrollWarning[]): WireWarning[] =>
	warnings.map((warning) => ({ ...warning, details: JSON.stringify(warning.details) }));

/** A write on a list with the row version from GetPayroll (null — without the check). */
type Versioned = { id: string; rowVersion: number | null };

// Company

export const listPayrollSettingsFn = createServerFn({ method: "GET" })
	.validator((companyId: string) => companyId)
	.handler(({ data }) => toResult(() => callApi(() => listPayrollSettings(data, apiOptions()))));

export const addPayrollSettingsFn = createServerFn({ method: "POST" })
	.validator((input: { companyId: string; request: AddPayrollSettingsRequest }) => input)
	.handler(({ data }) =>
		toResult(() => callApi(() => addPayrollSettings(data.companyId, data.request, apiOptions()))),
	);

export const listPayrollsFn = createServerFn({ method: "GET" })
	.validator((input: { companyId: string; year: number }) => input)
	.handler(({ data }) =>
		toResult(() => callApi(() => listPayrolls(data.companyId, { year: data.year }, apiOptions()))),
	);

export const createPayrollFn = createServerFn({ method: "POST" })
	.validator((input: { companyId: string; request: CreatePayrollRequest }) => input)
	.handler(({ data }) =>
		toResult(() => callApi(() => createPayroll(data.companyId, data.request, apiOptions()))),
	);

// List

export const getPayrollFn = createServerFn({ method: "GET" })
	.validator((id: string) => id)
	.handler(({ data }) => toResult(() => callApi(() => getPayroll(data, apiOptions()))));

export const deletePayrollFn = createServerFn({ method: "POST" })
	.validator((input: Versioned) => input)
	.handler(({ data }) =>
		toResult(() =>
			callApi(() =>
				deletePayroll(
					data.id,
					data.rowVersion === null ? undefined : { rowVersion: data.rowVersion },
					apiOptions(),
				),
			),
		),
	);

export const calculatePayrollFn = createServerFn({ method: "POST" })
	.validator((input: Versioned) => input)
	.handler(({ data }) =>
		toResult(() =>
			callApi(() => calculatePayroll(data.id, { rowVersion: data.rowVersion }, apiOptions())),
		),
	);

export const closePayrollFn = createServerFn({ method: "POST" })
	.validator((input: Versioned) => input)
	.handler(({ data }) =>
		toResult(() =>
			callApi(() => closePayroll(data.id, { rowVersion: data.rowVersion }, apiOptions())),
		),
	);

export const setPayrollPpkPaidFn = createServerFn({ method: "POST" })
	.validator((input: { id: string; request: SetPayrollPpkPaidRequest }) => input)
	.handler(({ data }) =>
		toResult(() => callApi(() => setPayrollPpkPaid(data.id, data.request, apiOptions()))),
	);

export const listPayrollMembersFn = createServerFn({ method: "GET" })
	.validator((id: string) => id)
	.handler(({ data }) => toResult(() => callApi(() => listPayrollMembers(data, apiOptions()))));

export const setPayrollMembersFn = createServerFn({ method: "POST" })
	.validator((input: { id: string; request: SetPayrollMembersRequest }) => input)
	.handler(({ data }) =>
		toResult(() => callApi(() => setPayrollMembers(data.id, data.request, apiOptions()))),
	);

export const listPayrollEntriesFn = createServerFn({ method: "GET" })
	.validator((id: string) => id)
	.handler(({ data }) =>
		toResult(async (): Promise<WireEntryItem[]> => {
			const entries = await callApi(() => listPayrollEntries(data, apiOptions()));
			return entries.map((entry) => ({ ...entry, warnings: wireWarnings(entry.warnings) }));
		}),
	);

// Payslip

export const getPayrollEntryFn = createServerFn({ method: "GET" })
	.validator((id: string) => id)
	.handler(({ data }) =>
		toResult(async (): Promise<WireEntry> => {
			const entry = await callApi(() => getPayrollEntry(data, apiOptions()));
			return {
				...entry,
				warnings: wireWarnings(entry.warnings),
				lines: entry.lines.map((line) => ({ ...line, trace: JSON.stringify(line.trace) })),
			};
		}),
	);

export const getYearTotalsFn = createServerFn({ method: "GET" })
	.validator((input: { employeeId: string; year: number }) => input)
	.handler(({ data }) =>
		toResult(() => callApi(() => getPayrollYearTotals(data.employeeId, data.year, apiOptions()))),
	);
