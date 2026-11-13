// Values and rules mirrored from db/changelog/modules/80-payroll/*.sql (payroll_app.payroll_*, payroll_app.entry_*,
// payroll.engine.sql, payroll.helpers.sql). The database computes everything; here they only choose what the screens
// offer and how the numbers read.

/** Tabs of the payroll page, kept in the URL (?tab=…). */
export const PAYROLL_TABS = ["lists", "settings"] as const;
export type PayrollTab = (typeof PAYROLL_TABS)[number];

/** Kinds of a list (R1): one regular list per company, kind and month, plus corrections of closed lists (R4). */
export const PAYROLL_KINDS = ["employment", "mandate", "specificWork", "correction"] as const;
export type PayrollKind = (typeof PAYROLL_KINDS)[number];
export const REGULAR_KINDS = ["employment", "mandate", "specificWork"] as const;

/** Statuses (R2) plus "stale" — a calculated list whose source data changed (is_stale), shown as its own state. */
export const PAYROLL_STATUSES = ["open", "calculated", "stale", "closed"] as const;
export type PayrollStatus = (typeof PAYROLL_STATUSES)[number];

export type ChipColor = "success" | "warning" | "danger" | "default" | "accent";

export const STATUS_COLORS: Record<PayrollStatus, ChipColor> = {
	open: "default",
	calculated: "accent",
	stale: "warning",
	closed: "success",
};

/** The state a person sees: a calculated list that went stale must be recalculated before closing. */
export function displayStatus(payroll: { status: string; isStale: boolean }): PayrollStatus {
	if (payroll.status === "calculated" && payroll.isStale) return "stale";
	return (PAYROLL_STATUSES as readonly string[]).includes(payroll.status)
		? (payroll.status as PayrollStatus)
		: "open";
}

/** Sides of a line (R24, payroll.lines.side). */
export const LINE_SIDES = ["income", "employee", "employer", "info", "deduction"] as const;
export type LineSide = (typeof LINE_SIDES)[number];

/** Codes the engine writes; a pay component's own code is not here and reads by the component name. */
export const LINE_CODES = [
	"ZASAD",
	"ZASAD-CH",
	"ZASAD-NB",
	"SW50",
	"CHOR",
	"ZAS-CH",
	"ZAS-ZUS",
	"URLOP-ZM",
	"NADG50",
	"NADG100",
	"ZLEC",
	"DZIELO",
	"EKWIW",
	"PODST-ZUS",
	"PODST-CHOR",
	"ZUS-E-P",
	"ZUS-R-P",
	"ZUS-C-P",
	"ZUS-E-PR",
	"ZUS-R-PR",
	"ZUS-W-PR",
	"FP",
	"FGSP",
	"PPK-P",
	"PPK-PD",
	"PPK-PR",
	"PPK-PRD",
	"PPK-PRZ",
	"ZDROW",
	"ZWOL-MLODZI",
	"KUP",
	"KUP50",
	"PODST-PIT",
	"PIT-ZMN",
	"PIT",
	"PIT-RYCZ",
	"ALIM",
	"KOMOR",
] as const;
export type LineCode = (typeof LINE_CODES)[number];

export function isLineCode(code: string): code is LineCode {
	return (LINE_CODES as readonly string[]).includes(code);
}

/** Tax lines (payroll.tax_codes() in payroll.engine.sql) — some are info, but they read together. */
const TAX_CODES: readonly string[] = [
	"KUP",
	"KUP50",
	"PODST-PIT",
	"ZWOL-MLODZI",
	"PIT-ZMN",
	"PIT",
	"PIT-RYCZ",
];

/** Groups of the payslip, in the order a payslip reads. */
export const LINE_GROUPS = [
	"income",
	"contributions",
	"tax",
	"ppk",
	"deductions",
	"employer",
	"info",
] as const;
export type LineGroup = (typeof LINE_GROUPS)[number];

export function lineGroup(line: { code: string; side: string }): LineGroup {
	if (TAX_CODES.includes(line.code)) return "tax";
	if (line.code.startsWith("PPK-")) return "ppk";
	switch (line.side) {
		case "income":
			return "income";
		case "employee":
			return "contributions";
		case "deduction":
			return "deductions";
		case "employer":
			return "employer";
		default:
			return "info";
	}
}

/**
 * Sum shown under a group: income, contributions, deductions and employer cost add up; tax counts only the advance
 * (PIT, PIT-RYCZ — the rest are bases and costs); PPK mixes the employee's and the employer's side and info lines do
 * not change any amount, so they have no sum.
 */
function groupTotal(group: LineGroup, lines: readonly { code: string; amount: number }[]) {
	if (group === "ppk" || group === "info") return null;
	const counted =
		group === "tax"
			? lines.filter((line) => line.code === "PIT" || line.code === "PIT-RYCZ")
			: lines;
	return roundCents(counted.reduce((sum, line) => sum + line.amount, 0));
}

export type LineGroupView<T> = { group: LineGroup; lines: T[]; total: number | null };

/** Lines of a payslip in payslip groups (empty groups dropped), keeping the engine's order inside a group. */
export function groupLines<T extends { code: string; side: string; amount: number; seq: number }>(
	lines: readonly T[],
): LineGroupView<T>[] {
	const sorted = [...lines].sort((a, b) => a.seq - b.seq);
	return LINE_GROUPS.map((group) => {
		const inGroup = sorted.filter((line) => lineGroup(line) === group);
		return { group, lines: inGroup, total: groupTotal(group, inGroup) };
	}).filter((view) => view.lines.length > 0);
}

export function roundCents(value: number): number {
	return Math.round((value + Number.EPSILON) * 100) / 100;
}

/** Amount columns of the payslip table, summed for the totals row. */
export const ENTRY_AMOUNTS = [
	"gross",
	"employeeContributions",
	"health",
	"pit",
	"ppkEmployee",
	"net",
	"deductions",
	"payout",
	"employerCost",
] as const;
export type EntryAmount = (typeof ENTRY_AMOUNTS)[number];

export function sumEntries(
	entries: readonly Record<EntryAmount, number>[],
): Record<EntryAmount, number> {
	const totals = Object.fromEntries(ENTRY_AMOUNTS.map((key) => [key, 0])) as Record<
		EntryAmount,
		number
	>;
	for (const entry of entries) {
		for (const key of ENTRY_AMOUNTS) totals[key] += entry[key];
	}
	for (const key of ENTRY_AMOUNTS) totals[key] = roundCents(totals[key]);
	return totals;
}

/** Warning codes of a payslip (db/README.md, payroll.entries.warnings). */
export const WARNING_CODES = [
	"pensionLimitReached",
	"peselMissing",
	"sicknessBaseMissing",
	"unconfirmedHours",
	"rateTimesHoursNotSupported",
	"leaveEntitlementMissing",
	"deductionLimited",
	"negativeTaxBase",
	"negativeCorrection",
	"deductionsNotRecalculated",
	"sicknessBaseUnknown",
	"sicknessBaseSupplement",
	"sicknessBaseFteChange",
	"sicknessWaitingPeriod",
	"overtimeNormalPayUnknown",
	"equivalentComponentsUnknown",
	"healthCapUnknown",
	"authorCostsLimitReached",
	"noAdvancesThresholdCrossed",
] as const;
export type WarningCode = (typeof WARNING_CODES)[number];

/** Warnings that stop the list from closing — cases the system does not calculate (payroll.blocking_warnings). */
export const BLOCKING_WARNINGS: readonly WarningCode[] = [
	"sicknessBaseMissing",
	"sicknessBaseUnknown",
	"sicknessBaseSupplement",
	"sicknessBaseFteChange",
	"sicknessWaitingPeriod",
	"overtimeNormalPayUnknown",
	"equivalentComponentsUnknown",
	"healthCapUnknown",
	"authorCostsLimitReached",
	"noAdvancesThresholdCrossed",
];

export function isBlockingWarning(code: string): boolean {
	return (BLOCKING_WARNINGS as readonly string[]).includes(code);
}

export function isWarningCode(code: string): code is WarningCode {
	return (WARNING_CODES as readonly string[]).includes(code);
}

// Trace (R24): a JSON object per line with the sources of the amount. Rendered as rows with labelled keys; legal
// parameters, identifiers and nested objects get their own shape so a person can read them without JSON.

export type TraceValue =
	| { type: "text"; text: string }
	| { type: "bool"; value: boolean }
	| { type: "param"; code: string; value: string; validFrom: string | null }
	| { type: "ids"; ids: string[] }
	| { type: "group" };

export type TraceRow = { id: string; key: string; depth: number; value: TraceValue };

const NUMBER = new Intl.NumberFormat("pl-PL", { maximumFractionDigits: 6, useGrouping: true });
const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** Number in a trace: Polish separators, up to 6 decimals (rates like 0.0976, averages to 0.0001). */
export function formatTraceNumber(value: number): string {
	return NUMBER.format(value);
}

/** First block of an identifier, enough to tell entries apart on screen. */
export function shortId(id: string): string {
	return UUID.test(id) ? id.slice(0, 8) : id;
}

function scalar(value: unknown): TraceValue {
	if (value === null || value === undefined) return { type: "text", text: "—" };
	if (typeof value === "boolean") return { type: "bool", value };
	if (typeof value === "number") return { type: "text", text: formatTraceNumber(value) };
	if (typeof value === "string")
		return { type: "text", text: UUID.test(value) ? shortId(value) : value };
	return { type: "text", text: JSON.stringify(value) };
}

function isPlainObject(value: unknown): value is Record<string, unknown> {
	return typeof value === "object" && value !== null && !Array.isArray(value);
}

function paramRows(params: Record<string, unknown>, depth: number, path: string): TraceRow[] {
	return Object.entries(params).map(([code, param]) => {
		const value = isPlainObject(param) ? param.value : param;
		const validFrom =
			isPlainObject(param) && typeof param.validFrom === "string" ? param.validFrom : null;
		const text = typeof value === "number" ? formatTraceNumber(value) : String(value ?? "—");
		return {
			id: `${path}.${code}`,
			key: "param",
			depth,
			value: { type: "param", code, value: text, validFrom },
		};
	});
}

/**
 * Trace object → rows. `params` becomes one row per legal parameter (value and the date it is valid from), arrays of
 * identifiers one row with short ids, nested objects a heading row with indented rows, arrays of objects numbered
 * headings (e.g. months of the sickness base).
 */
export function traceRows(trace: unknown, depth = 0, path = ""): TraceRow[] {
	if (!isPlainObject(trace)) return [];
	const rows: TraceRow[] = [];
	for (const [key, value] of Object.entries(trace)) {
		const id = path ? `${path}.${key}` : key;
		if (key === "params" && isPlainObject(value)) {
			rows.push(...paramRows(value, depth, id));
		} else if (Array.isArray(value)) {
			if (value.every((item) => typeof item === "string")) {
				rows.push({
					id,
					key,
					depth,
					value: { type: "ids", ids: value.map((item) => shortId(item)) },
				});
			} else if (value.every(isPlainObject)) {
				rows.push({ id, key, depth, value: { type: "group" } });
				value.forEach((item, index) => {
					const itemId = `${id}.${index}`;
					rows.push({
						id: itemId,
						key: String(index + 1),
						depth: depth + 1,
						value: { type: "group" },
					});
					rows.push(...traceRows(item, depth + 2, itemId));
				});
			} else {
				rows.push({
					id,
					key,
					depth,
					value: {
						type: "text",
						text: value
							.map((item) => scalar(item))
							.map(textOf)
							.join(", "),
					},
				});
			}
		} else if (isPlainObject(value)) {
			rows.push({ id, key, depth, value: { type: "group" } });
			rows.push(...traceRows(value, depth + 1, id));
		} else {
			rows.push({ id, key, depth, value: scalar(value) });
		}
	}
	return rows;
}

function textOf(value: TraceValue): string {
	return value.type === "text" ? value.text : value.type === "bool" ? String(value.value) : "";
}

/** Rate of a line as a percentage: 0.0976 → "9,76%" (rates are fractions; 1 = 100%). */
export function formatLineRate(rate: number | null | undefined): string {
	if (rate === null || rate === undefined) return "";
	return `${new Intl.NumberFormat("pl-PL", { maximumFractionDigits: 4 }).format(rate * 100)}%`;
}

/** Accident rate typed as a percentage ("1,67") → fraction for the API (0.0167); malformed → null. */
export function accidentRateFromPercent(value: string): number | null {
	if (!/^\s*\d{1,2}([.,]\d{1,2})?\s*$/.test(value)) return null;
	return Math.round(Number(value.trim().replace(",", ".")) * 100) / 10000;
}

/** 0.0167 → "1,67" for the percentage field. */
export function accidentRatePercentText(rate: number): string {
	return String(Math.round(rate * 10000) / 100).replace(".", ",");
}

/** Last day of the month of a YYYY-MM-DD date. */
export function lastOfMonth(date: string): string {
	const year = Number(date.slice(0, 4));
	const month = Number(date.slice(5, 7));
	const day = new Date(Date.UTC(year, month, 0)).getUTCDate();
	return `${date.slice(0, 7)}-${String(day).padStart(2, "0")}`;
}

/** The latest legal payment date of a month's list: the 10th of the next month (KP art. 85 § 2). */
export function latestPaymentDate(period: string): string {
	const year = Number(period.slice(0, 4));
	const month = Number(period.slice(5, 7));
	return month === 12 ? `${year + 1}-01-10` : `${year}-${String(month + 1).padStart(2, "0")}-10`;
}

/** "2026-11-01" → "11.2026". */
export function formatPeriod(periodFrom: string): string {
	return `${periodFrom.slice(5, 7)}.${periodFrom.slice(0, 4)}`;
}

/** Months a new list can be for: January to December of the year, as YYYY-MM-01. */
export function monthOptions(year: number): string[] {
	return Array.from(
		{ length: 12 },
		(_, index) => `${year}-${String(index + 1).padStart(2, "0")}-01`,
	);
}

/** Changes of the members step against what the server has: who to exclude and who to bring back. */
export function memberChanges(
	members: readonly { employmentId: string; excluded: boolean }[],
	excluded: ReadonlySet<string>,
): { exclude: string[]; include: string[] } {
	return {
		exclude: members
			.filter((member) => !member.excluded && excluded.has(member.employmentId))
			.map((member) => member.employmentId),
		include: members
			.filter((member) => member.excluded && !excluded.has(member.employmentId))
			.map((member) => member.employmentId),
	};
}
