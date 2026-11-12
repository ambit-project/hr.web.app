import type { AddEmploymentTermsRequest, GetEmploymentTerms } from "#/api/models";

// Values and rules mirrored from db/changelog/1-ddl/20261112-14-employment.sql and
// db/changelog/modules/30-employment/employment.helpers.sql (terms_validate). The database stays the authority;
// here they only choose the fields and defaults the form shows.
export const EMPLOYMENT_KINDS = ["employment", "mandate", "specific_work"] as const;
export type EmploymentKind = (typeof EMPLOYMENT_KINDS)[number];

export const CONTRACT_TYPES = ["trial", "fixed_term", "indefinite"] as const;
export type ContractType = (typeof CONTRACT_TYPES)[number];

export const EMPLOYMENT_STATUSES = ["future", "active", "ended"] as const;
export type EmploymentStatus = (typeof EMPLOYMENT_STATUSES)[number];

export const PAY_BASES = ["monthly", "hourly", "lumpSum"] as const;
export type PayBasis = (typeof PAY_BASES)[number];

export const COST_KINDS = ["standard", "increased", "flat20", "author50"] as const;
export type CostKind = (typeof COST_KINDS)[number];

export const INSURANCES = [
	"pension",
	"disability",
	"sickness",
	"accident",
	"health",
	"fp",
	"fgsp",
] as const;
export type Insurance = (typeof INSURANCES)[number];

export const PAY_BASES_BY_KIND: Record<EmploymentKind, readonly PayBasis[]> = {
	employment: ["monthly"],
	mandate: ["hourly", "lumpSum"],
	specific_work: ["lumpSum"],
};

export const COST_KINDS_BY_KIND: Record<EmploymentKind, readonly CostKind[]> = {
	employment: ["standard", "increased"],
	mandate: ["flat20", "author50"],
	specific_work: ["flat20", "author50"],
};

/** Position and FTE belong to an employment contract only; ZUS codes to employment and mandate. */
export const hasPosition = (kind: EmploymentKind) => kind === "employment";
export const hasZusCodes = (kind: EmploymentKind) => kind !== "specific_work";

/**
 * Insurance sets offered as one click. A mandate's set depends on facts outside the system (another title elsewhere,
 * a student under 26), so Kadry choose; the database checks only that the set is consistent.
 */
export const INSURANCE_PRESETS = {
	employment: ["pension", "disability", "sickness", "accident", "health", "fp", "fgsp"],
	mandateSoleTitle: ["pension", "disability", "accident", "health"],
	mandateHealthOnly: ["health"],
	none: [],
} as const satisfies Record<string, readonly Insurance[]>;

export function defaultInsurances(kind: EmploymentKind): Insurance[] {
	if (kind === "employment") return [...INSURANCE_PRESETS.employment];
	if (kind === "mandate") return [...INSURANCE_PRESETS.mandateSoleTitle];
	return [];
}

/** Terms fields of the form; numbers stay text until sent. */
export type TermsValues = {
	positionId: string;
	fteNumerator: string;
	fteDenominator: string;
	payBasis: string;
	baseAmount: string;
	zusTitleCode: string;
	zusPensionCode: string;
	zusDisabilityCode: string;
	insurances: string[];
	costKind: string;
};

export function emptyTerms(kind: EmploymentKind): TermsValues {
	return {
		positionId: "",
		fteNumerator: hasPosition(kind) ? "1" : "",
		fteDenominator: hasPosition(kind) ? "1" : "",
		payBasis: PAY_BASES_BY_KIND[kind][0],
		baseAmount: "",
		zusTitleCode: kind === "employment" ? "0110" : kind === "mandate" ? "0411" : "",
		zusPensionCode: hasZusCodes(kind) ? "0" : "",
		zusDisabilityCode: hasZusCodes(kind) ? "0" : "",
		insurances: defaultInsurances(kind),
		costKind: COST_KINDS_BY_KIND[kind][0],
	};
}

export function termsValues(terms: GetEmploymentTerms): TermsValues {
	return {
		positionId: terms.positionId ?? "",
		fteNumerator: terms.fteNumerator?.toString() ?? "",
		fteDenominator: terms.fteDenominator?.toString() ?? "",
		payBasis: terms.payBasis,
		baseAmount: terms.baseAmount.toFixed(2),
		zusTitleCode: terms.zusTitleCode ?? "",
		zusPensionCode: terms.zusPensionCode ?? "",
		zusDisabilityCode: terms.zusDisabilityCode ?? "",
		insurances: [...terms.insurances],
		costKind: terms.costKind,
	};
}

const blank = (value: string) => (value.trim() === "" ? null : value.trim());
const integer = (value: string) => (value.trim() === "" ? null : Number.parseInt(value, 10));

/** "6 500,50" → 6500.5; empty → null (the database answers "required"). */
export function parseAmount(value: string): number | null {
	const normalized = value.replace(/\s/g, "").replace(",", ".");
	return normalized === "" ? null : Number(normalized);
}

/** Request fields for the kind: fields the kind does not have are sent empty, as the database expects. */
export function termsRequest(
	kind: EmploymentKind,
	values: TermsValues,
): Omit<AddEmploymentTermsRequest, "validFrom"> {
	return {
		positionId: hasPosition(kind) ? blank(values.positionId) : null,
		fteNumerator: hasPosition(kind) ? integer(values.fteNumerator) : null,
		fteDenominator: hasPosition(kind) ? integer(values.fteDenominator) : null,
		payBasis: blank(values.payBasis),
		baseAmount: parseAmount(values.baseAmount),
		zusTitleCode: hasZusCodes(kind) ? blank(values.zusTitleCode) : null,
		zusPensionCode: hasZusCodes(kind) ? blank(values.zusPensionCode) : null,
		zusDisabilityCode: hasZusCodes(kind) ? blank(values.zusDisabilityCode) : null,
		insurances: kind === "specific_work" ? [] : values.insurances,
		costKind: blank(values.costKind),
	};
}

export const AMOUNT_PATTERN = /^\s*\d[\d\s]*([.,]\d{1,2})?\s*$/;

export function formatAmount(amount: number): string {
	return new Intl.NumberFormat("pl-PL", {
		minimumFractionDigits: 2,
		maximumFractionDigits: 2,
	}).format(amount);
}

export function formatFte(
	numerator: number | null | undefined,
	denominator: number | null | undefined,
) {
	return numerator && denominator ? `${numerator}/${denominator}` : "—";
}
