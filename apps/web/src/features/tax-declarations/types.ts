// Values and rules mirrored from db/changelog/1-ddl/20261113-16-tax-declarations.sql and
// db/changelog/modules/40-tax-declarations/tax_declarations.helpers.sql. The database stays the authority; here they
// only choose the fields and the months the form offers.
export const DECLARATION_KINDS = [
	"pit2",
	"youthReliefWaiver",
	"returnRelief",
	"familyRelief",
	"seniorRelief",
	"increasedCosts",
	"employeeCostsWaiver",
	"authorCostsWaiver",
	"noAdvances",
	"higherRate",
] as const;
export type DeclarationKind = (typeof DECLARATION_KINDS)[number];

export const PIT2_SHARES = ["1/12", "1/24", "1/36"] as const;

export const DECLARATION_STATUSES = ["future", "active", "ended", "lapsed"] as const;
export type DeclarationStatus = (typeof DECLARATION_STATUSES)[number];

/** First day of the month of `date` (YYYY-MM-DD) and of the next month, as YYYY-MM-01. */
function monthStart(year: number, monthIndex: number): string {
	const normalized = new Date(Date.UTC(year, monthIndex, 1));
	return normalized.toISOString().slice(0, 10);
}

/**
 * Months a declaration (or its withdrawal) may take effect from: the month it was handed in or the next one
 * (art. 31a ust. 5 u.p.d.o.f.). Empty when the date is missing or malformed.
 */
export function allowedMonths(date: string): string[] {
	const match = /^(\d{4})-(\d{2})-\d{2}$/.exec(date);
	if (!match) return [];
	const year = Number(match[1]);
	const monthIndex = Number(match[2]) - 1;
	return [monthStart(year, monthIndex), monthStart(year, monthIndex + 1)];
}

/** "2026-04-01" → "04.2026". */
export function formatMonth(month: string): string {
	return `${month.slice(5, 7)}.${month.slice(0, 4)}`;
}
