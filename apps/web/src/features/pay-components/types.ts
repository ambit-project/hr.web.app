import type { ListComponentTypesItem, ListConsentsItem } from "#/api/models";
import { formatDecimal, formatMoney, parseAmount } from "#/lib/money";

// Values and rules mirrored from db/changelog/1-ddl/20261113-18-pay-components.sql and
// db/changelog/modules/50-pay-components/pay_components.helpers.sql. The database stays the authority; here they only
// choose the fields and options the forms show.
export const COMPONENT_KINDS = ["addition", "deduction"] as const;
export type ComponentKind = (typeof COMPONENT_KINDS)[number];

export const CALCULATIONS = ["fixed", "percentOfBase", "rateTimesHours", "engine"] as const;
export type Calculation = (typeof CALCULATIONS)[number];

/** A deduction is a fixed amount only; `engine` belongs to system components (component_type_validate). */
export function calculationsFor(kind: ComponentKind): Calculation[] {
	return kind === "addition" ? ["fixed", "percentOfBase", "rateTimesHours"] : ["fixed"];
}

export const DEDUCTION_CATEGORIES = [
	"advance",
	"penalty",
	"voluntaryEmployer",
	"voluntaryOther",
	"alimony",
	"enforcement",
] as const;
export type DeductionCategory = (typeof DEDUCTION_CATEGORIES)[number];

/** Alimony and enforcement belong to the system components ALIM and KOMOR; the Administrator picks the rest. */
export const SELECTABLE_CATEGORIES = [
	"advance",
	"penalty",
	"voluntaryEmployer",
	"voluntaryOther",
] as const satisfies readonly DeductionCategory[];

/** Voluntary deductions need the employee's written consent (KP art. 91; pay_components.requires_consent). */
export function requiresConsent(category: string | null | undefined): boolean {
	return category === "voluntaryEmployer" || category === "voluntaryOther";
}

export const ADDITION_FLAGS = ["inZusBase", "inHealthBase", "taxable", "inSicknessBase"] as const;
export type AdditionFlag = (typeof ADDITION_FLAGS)[number];

/** Flags of an addition that are set, in table order; a deduction has none. */
export function setFlags(item: Pick<ListComponentTypesItem, AdditionFlag>): AdditionFlag[] {
	return ADDITION_FLAGS.filter((flag) => item[flag] === true);
}

export const COMPONENT_STATUSES = ["future", "active", "ended"] as const;
export type ComponentStatus = (typeof COMPONENT_STATUSES)[number];

export const CONSENT_STATUSES = ["unsigned", "future", "active", "ended"] as const;
export type ConsentStatus = (typeof CONSENT_STATUSES)[number];

export const GARNISHMENT_KINDS = ["alimony", "other"] as const;
export type GarnishmentKind = (typeof GARNISHMENT_KINDS)[number];

export const AUTHORITY_KINDS = ["court", "administrative", "creditor"] as const;
export type AuthorityKind = (typeof AUTHORITY_KINDS)[number];

/** A creditor collects without enforcement only alimony (KP art. 88). */
export function authorityKindsFor(kind: GarnishmentKind): AuthorityKind[] {
	return kind === "alimony" ? [...AUTHORITY_KINDS] : ["court", "administrative"];
}

export const GARNISHMENT_STATUSES = ["active", "paidOff", "discontinued"] as const;
export type GarnishmentStatus = (typeof GARNISHMENT_STATUSES)[number];

/** Types Kadry may put on a contract: active, not system and — on a mandate or specific work — no penalty (KP art. 108). */
export function assignableTypes(
	types: readonly ListComponentTypesItem[],
	employmentKind: string,
): ListComponentTypesItem[] {
	return types.filter(
		(type) =>
			type.isActive &&
			!type.isSystem &&
			(employmentKind === "employment" || type.deductionCategory !== "penalty"),
	);
}

/** Signed consents of the employee for one component type — the ones a contract component may point to. */
export function signedConsentsFor(
	consents: readonly ListConsentsItem[],
	componentTypeId: string,
): ListConsentsItem[] {
	return consents.filter(
		(consent) => consent.componentTypeId === componentTypeId && consent.signedAt !== null,
	);
}

/** Contract component value as the table shows it: an amount, a percent of base pay or an hourly rate. */
export function formatComponentValue(calculation: string, value: number): string {
	if (calculation === "percentOfBase") return `${formatDecimal(value)}%`;
	if (calculation === "rateTimesHours") return `${formatMoney(value)}/godz.`;
	return formatMoney(value);
}

/** "2026-01-01 – …" — a period with an open end. */
export function formatPeriod(from: string, to: string | null | undefined): string {
	return `${from} – ${to ?? "…"}`;
}

/** Garnishment form fields; amounts stay text until sent. */
export type GarnishmentValues = {
	kind: GarnishmentKind;
	authorityKind: string;
	authorityName: string;
	caseNumber: string;
	receivedAt: string;
	monthlyAmount: string;
	totalAmount: string;
	note: string;
};

const blank = (value: string) => (value.trim() === "" ? null : value.trim());

/** Request body shared by create and update; empty texts and amounts go as null (the database answers "required"). */
export function garnishmentRequest(values: GarnishmentValues) {
	return {
		kind: values.kind,
		authorityKind: blank(values.authorityKind),
		authorityName: blank(values.authorityName),
		caseNumber: blank(values.caseNumber),
		receivedAt: blank(values.receivedAt),
		monthlyAmount: parseAmount(values.monthlyAmount),
		totalAmount: parseAmount(values.totalAmount),
		note: blank(values.note),
	};
}
