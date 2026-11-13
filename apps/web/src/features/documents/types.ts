/** How a PIT-11 was handed to the taxpayer (documents.tax_forms.given_how). */
export const GIVEN_HOWS = ["paper", "email", "portal"] as const;
export type GivenHow = (typeof GIVEN_HOWS)[number];

/** Purpose of a form (poz. 7): filing (1) or correction (2). */
export const TAX_FORM_PURPOSES = ["filing", "correction"] as const;
export type TaxFormPurpose = (typeof TAX_FORM_PURPOSES)[number];

/** Data a form cannot go out without (data.missing from documents.pit11_compute). */
export const MISSING_FIELDS = ["pesel", "address", "taxOffice", "ppkIncomeRelief"] as const;
export type MissingField = (typeof MISSING_FIELDS)[number];

export function isMissingField(value: string): value is MissingField {
	return (MISSING_FIELDS as readonly string[]).includes(value);
}

/** Row of a PIT-11 list as the company screen shows it. */
export type Pit11State = "notGenerated" | "missingData" | "generated" | "given" | "sent";

export function pit11State(item: {
	taxFormId: string | null;
	missing: readonly string[];
	sentToOfficeAt: string | null;
	givenToEmployeeAt: string | null;
}): Pit11State {
	if (item.taxFormId === null) return "notGenerated";
	if (item.missing.length > 0) return "missingData";
	if (item.sentToOfficeAt !== null) return "sent";
	if (item.givenToEmployeeAt !== null) return "given";
	return "generated";
}

/**
 * Deadlines of PIT-11 for a tax year (PIT act art. 42g ust. 1): the tax office by the end of January, the taxpayer
 * by the end of February of the next year.
 */
export function pit11Deadlines(year: number): { office: string; taxpayer: string } {
	const next = year + 1;
	const februaryEnd = new Date(Date.UTC(next, 2, 0)).getUTCDate();
	return { office: `${next}-01-31`, taxpayer: `${next}-02-${februaryEnd}` };
}

/** One column of part E: position number on the form or null where the form has no field. */
type Cell = number | null;

/**
 * Part E of PIT-11(29): rows 1–15 with positions of columns b (income), c (costs), d (income − costs), e (exempt
 * income) and f (advance), as printed on the form. Positions match the XSD names (P_29 …).
 */
export const PIT11_PART_E: readonly {
	row: number;
	cells: readonly [Cell, Cell, Cell, Cell, Cell];
}[] = [
	{ row: 1, cells: [29, 30, 31, 32, 33] },
	{ row: 2, cells: [36, 37, 38, 39, 40] },
	{ row: 3, cells: [43, 44, 45, 46, 47] },
	{ row: 4, cells: [50, null, 51, 52, 53] },
	{ row: 5, cells: [54, 55, 56, null, 57] },
	{ row: 6, cells: [58, 59, 60, null, 61] },
	{ row: 7, cells: [62, 63, 64, null, 65] },
	{ row: 8, cells: [66, 67, 68, null, 69] },
	{ row: 9, cells: [70, null, 71, null, 72] },
	{ row: 10, cells: [75, null, 76, null, 77] },
	{ row: 11, cells: [78, null, 79, null, 80] },
	{ row: 12, cells: [81, null, 82, null, 83] },
	{ row: 13, cells: [84, null, 85, null, 86] },
	{ row: 14, cells: [87, null, 88, null, 89] },
	{ row: 15, cells: [90, 91, 92, 93, 94] },
];

/** Row label key of part E ("1" … "15"). */
export type PartERow =
	| "1"
	| "2"
	| "3"
	| "4"
	| "5"
	| "6"
	| "7"
	| "8"
	| "9"
	| "10"
	| "11"
	| "12"
	| "13"
	| "14"
	| "15";

/** Advance columns (f) are whole złoty on the form. */
const WHOLE_POSITIONS = new Set([
	11, 28, 33, 40, 47, 53, 57, 61, 65, 69, 72, 77, 80, 83, 86, 89, 94, 118, 119, 120, 121,
]);

export function isWholePosition(position: number): boolean {
	return WHOLE_POSITIONS.has(position);
}

/** Value of a position from the form data; undefined when the field stays empty. */
export function positionValue(
	positions: Readonly<Record<string, number>>,
	position: number,
): number | undefined {
	return positions[`P_${position}`];
}

/** Part G amounts in form order (poz. 106–117, 122–123); 118–121 are tick boxes shown separately. */
export const PIT11_PART_G_AMOUNTS = [
	106, 107, 108, 109, 110, 111, 112, 113, 114, 115, 116, 117, 122, 123,
] as const;

/** Sums of wage card rows (all amount columns). */
export const WAGE_CARD_AMOUNTS = [
	"gross",
	"zusBase",
	"employeeContributions",
	"employerContributions",
	"health",
	"costs",
	"pit",
	"ppkEmployee",
	"ppkEmployer",
	"deductions",
	"net",
	"payout",
] as const;
export type WageCardAmount = (typeof WAGE_CARD_AMOUNTS)[number];

export function sumWageCard(
	rows: readonly Record<WageCardAmount, number>[],
): Record<WageCardAmount, number> {
	const totals = Object.fromEntries(WAGE_CARD_AMOUNTS.map((key) => [key, 0])) as Record<
		WageCardAmount,
		number
	>;
	for (const row of rows) {
		for (const key of WAGE_CARD_AMOUNTS) totals[key] += row[key];
	}
	for (const key of WAGE_CARD_AMOUNTS) totals[key] = Math.round(totals[key] * 100) / 100;
	return totals;
}

/** Today in the browser's time zone as YYYY-MM-DD (default day of a "sent" or "given" mark). */
export function todayIso(now: Date = new Date()): string {
	const month = String(now.getMonth() + 1).padStart(2, "0");
	const day = String(now.getDate()).padStart(2, "0");
	return `${now.getFullYear()}-${month}-${day}`;
}

/** File name of a downloaded PIT-11 XML (the API sends the same name in Content-Disposition). */
export function xmlFileName(form: { year: number; employeeNumber: string; version: number }) {
	return `PIT-11_${form.year}_${form.employeeNumber}_v${form.version}.xml`;
}
