import type { ListAbsenceTypesItem } from "#/api/models";
import type { EmploymentKind } from "#/features/employments/types";

// Values and rules mirrored from db/changelog/1-ddl/20261113-26-time-absence.sql and
// db/changelog/modules/60-time-absence/*.sql. The database stays the authority; here they only choose what the
// screens offer.
export const DAY_KINDS = ["workday", "dayOff", "holiday", "designatedOff"] as const;
export type DayKind = (typeof DAY_KINDS)[number];

export const CONFIRMATION_FORMS = ["written", "electronic", "documentary"] as const;
export type ConfirmationForm = (typeof CONFIRMATION_FORMS)[number];

export const OVERTIME_RATES = ["50", "100"] as const;
export type OvertimeRate = (typeof OVERTIME_RATES)[number];

/** Leave base chosen by Kadry from the person's seniority (KP art. 154 § 1). */
export const LEAVE_BASES = ["20", "26"] as const;
export type LeaveBase = (typeof LEAVE_BASES)[number];

/** One leave day is 8 hours (KP art. 154² § 2); the balance in days is hours ÷ 8. */
export const HOURS_PER_DAY = 8;

/** Overtime warning threshold in a year (KP art. 151 § 3), as the API also reports it. */
export const OVERTIME_YEAR_LIMIT = 150;

const NUMBER = new Intl.NumberFormat("pl-PL", { maximumFractionDigits: 2, useGrouping: true });

/** 8 → "8", 7.5 → "7,5", 13.333 → "13,33". */
export function formatNumber(value: number): string {
	return NUMBER.format(value);
}

/** 8 → "8 h". */
export function formatHours(hours: number): string {
	return `${formatNumber(hours)} h`;
}

/** Hours as leave days (÷ 8), rounded to two decimals. */
export function hoursToDays(hours: number): number {
	return Math.round((hours / HOURS_PER_DAY) * 100) / 100;
}

/** Hours typed by a person: digits with up to two decimals after "," or ".". */
export const HOURS_PATTERN = /^\s*\d{1,4}([.,]\d{1,2})?\s*$/;

/** "7,5" → 7.5; empty → null. */
export function parseHours(value: string): number | null {
	const normalized = value.trim().replace(",", ".");
	return normalized === "" ? null : Number(normalized);
}

/** 7.5 → "7,5" for an hours field (parses back unchanged). */
export function hoursText(hours: number | null | undefined): string {
	return hours === null || hours === undefined ? "" : String(hours).replace(".", ",");
}

/**
 * What the database said is left when a limit was hit: `remainingDays` comes with `limitExceeded` (days per year or
 * per event), `remainingHours` with `leaveLimitExceeded` (leave balance). They are values, not codes.
 */
export function remainingFrom(
	errors: Record<string, string[]> | undefined,
): { unit: "days" | "hours"; value: number } | null {
	const days = errors?.remainingDays?.[0];
	if (days !== undefined && Number.isFinite(Number(days))) {
		return { unit: "days", value: Number(days) };
	}
	const hours = errors?.remainingHours?.[0];
	if (hours !== undefined && Number.isFinite(Number(hours))) {
		return { unit: "hours", value: Number(hours) };
	}
	return null;
}

/**
 * Absence types a contract allows (R8): an employment contract all, a mandate only sickness and only with sickness
 * insurance, specific work none. The insurance on the start day is checked by the database.
 */
export function allowedAbsenceTypes(
	types: readonly ListAbsenceTypesItem[],
	kind: EmploymentKind,
	hasSicknessInsurance: boolean,
): ListAbsenceTypesItem[] {
	if (kind === "employment") return [...types];
	if (kind === "mandate" && hasSicknessInsurance) return types.filter((type) => type.sickness);
	return [];
}

export type DayCell<T> = { date: string; day: number; data: T | undefined };

/**
 * Weeks of a month, Monday first, for the calendar grid: each week has seven places, empty (null) before the first and
 * after the last day. Days from the API are matched by date; a missing one stays without data.
 */
export function monthWeeks<T extends { date: string }>(
	year: number,
	month: number,
	days: readonly T[],
): (DayCell<T> | null)[][] {
	const byDate = new Map(days.map((day) => [day.date, day]));
	const count = new Date(Date.UTC(year, month, 0)).getUTCDate();
	// getUTCDay: Sunday 0 … Saturday 6; Monday-first offset.
	const offset = (new Date(Date.UTC(year, month - 1, 1)).getUTCDay() + 6) % 7;
	const cells: (DayCell<T> | null)[] = Array.from({ length: offset }, () => null);
	for (let day = 1; day <= count; day++) {
		const date = `${year}-${String(month).padStart(2, "0")}-${String(day).padStart(2, "0")}`;
		cells.push({ date, day, data: byDate.get(date) });
	}
	while (cells.length % 7 !== 0) cells.push(null);
	const weeks: (DayCell<T> | null)[][] = [];
	for (let start = 0; start < cells.length; start += 7) weeks.push(cells.slice(start, start + 7));
	return weeks;
}

/** Monday 1 … Sunday 7 for a YYYY-MM-DD date. */
export function isoWeekday(date: string): number {
	const [year, month, day] = date.split("-").map(Number);
	return ((new Date(Date.UTC(year, month - 1, day)).getUTCDay() + 6) % 7) + 1;
}

/**
 * Kinds a day may take (R4, calendar_day_set): a dictionary holiday stays a holiday, any other day cannot become one;
 * a day off for a Saturday holiday only Monday to Friday. The per-month count is checked by the database.
 */
export function dayKindsFor(date: string, isHoliday: boolean): DayKind[] {
	if (isHoliday) return ["holiday"];
	return isoWeekday(date) <= 5 ? ["workday", "dayOff", "designatedOff"] : ["workday", "dayOff"];
}

const MONTH_NAME = new Intl.DateTimeFormat("pl-PL", { month: "long", timeZone: "UTC" });

/** 1 → "Styczeń". */
export function monthName(month: number): string {
	const name = MONTH_NAME.format(new Date(Date.UTC(2000, month - 1, 1)));
	return name.charAt(0).toUpperCase() + name.slice(1);
}

/** Years offered in a year picker: two back and one ahead of the current one, plus the chosen one. */
export function yearOptions(current: number, selected: number): number[] {
	const years = new Set([current - 2, current - 1, current, current + 1, selected]);
	return [...years].sort((a, b) => a - b);
}

/** Current year in the Polish time zone (the server renders in UTC, which differs on New Year's Eve night). */
export function currentYear(now = new Date()): number {
	return Number(
		new Intl.DateTimeFormat("en-GB", { year: "numeric", timeZone: "Europe/Warsaw" }).format(now),
	);
}

/** What the time sections of a contract page need to know about the contract. */
export type TimeContract = {
	id: string;
	kind: EmploymentKind;
	hasSicknessInsurance: boolean;
	hourly: boolean;
};

type ContractLike = {
	id: string;
	kind: string;
	termsHistory: readonly { insurances: readonly string[]; payBasis: string }[];
};

/**
 * Sickness insurance and hourly pay in any terms version: the database checks the version of the start day, the
 * screen only decides which sections and absence types to offer.
 */
export function timeContract(contract: ContractLike): TimeContract {
	return {
		id: contract.id,
		kind: contract.kind as EmploymentKind,
		hasSicknessInsurance: contract.termsHistory.some((terms) =>
			terms.insurances.includes("sickness"),
		),
		hourly: contract.termsHistory.some((terms) => terms.payBasis === "hourly"),
	};
}

/** Year shown first: the current one, moved into the contract's period when the contract is over or not yet started. */
export function defaultYear(
	contract: { startDate: string; endDate: string | null; terminatedAt: string | null },
	current: number,
): number {
	const start = Number(contract.startDate.slice(0, 4));
	const endDate = contract.terminatedAt ?? contract.endDate;
	const end = endDate ? Number(endDate.slice(0, 4)) : Number.POSITIVE_INFINITY;
	return Math.min(Math.max(current, start), end);
}
