// Values and rules mirrored from db/changelog/modules/70-ppk/*.sql (ppk_app.enrollment_candidates,
// ppk_app.participation_*, ppk_app.declaration_*, ppk.reenrollment_rows). The database stays the authority; here they
// only choose what the screens offer and how they read.

/** Tabs of the PPK page, kept in the URL (?tab=…). */
export const PPK_TABS = ["settings", "candidates", "reenrollment"] as const;
export type PpkTab = (typeof PPK_TABS)[number];

/** Participant status on a day (R11, ppk.status_on). */
export const PARTICIPATION_STATUSES = ["active", "resigned", "notEnrolled"] as const;
export type ParticipationStatus = (typeof PARTICIPATION_STATUSES)[number];

/** Status on the "do zapisu" list (R5): waiting for 3 months, to enrol, deadline passed, 55–69 without a request. */
export const CANDIDATE_STATUSES = ["waiting", "toEnroll", "overdue", "requestOnly"] as const;
export type CandidateStatus = (typeof CANDIDATE_STATUSES)[number];

/** Groups of the re-enrolment list (R13, art. 23 ust. 5–9): under 55, 55–69, 70 and over. */
export const REENROLLMENT_GROUPS = ["restart", "requestOnly", "noRestart"] as const;
export type ReenrollmentGroup = (typeof REENROLLMENT_GROUPS)[number];

/** Declarations Kadry enter (R9); `autoRestart` is written only by the 4-year cycle. */
export const DECLARATION_KINDS = [
	"resignation",
	"restartRequest",
	"enrollmentRequest",
	"reducedRate",
	"additionalRate",
] as const;
export type DeclarationKind = (typeof DECLARATION_KINDS)[number];
export type AnyDeclarationKind = DeclarationKind | "autoRestart";

/** Only rate declarations carry a rate: reduced basic 0.5–2 %, employee additional 0–2 % (art. 27 ust. 2–3). */
export const RATE_LIMITS: Record<"reducedRate" | "additionalRate", { min: number; max: number }> = {
	reducedRate: { min: 0.5, max: 2 },
	additionalRate: { min: 0, max: 2 },
};

export function kindHasRate(kind: string): kind is "reducedRate" | "additionalRate" {
	return kind === "reducedRate" || kind === "additionalRate";
}

export type ChipColor = "success" | "warning" | "danger" | "default" | "accent";

export const PARTICIPATION_COLORS: Record<ParticipationStatus, ChipColor> = {
	active: "success",
	resigned: "warning",
	notEnrolled: "default",
};

export const CANDIDATE_COLORS: Record<CandidateStatus, ChipColor> = {
	waiting: "default",
	toEnroll: "accent",
	overdue: "danger",
	requestOnly: "warning",
};

/** Chip colour of a status from the API; an unknown value (newer backend) reads as neutral. */
export function statusColor<T extends string>(colors: Record<T, ChipColor>, status: string) {
	return (colors as Record<string, ChipColor>)[status] ?? "default";
}

const NUMBER = new Intl.NumberFormat("pl-PL", { maximumFractionDigits: 2 });

/** 2 → "2%", 1.5 → "1,5%", 0.75 → "0,75%". */
export function formatRate(rate: number | null | undefined): string {
	return rate === null || rate === undefined ? "—" : `${NUMBER.format(rate)}%`;
}

/** A rate typed by a person: digits with up to two decimals after "," or ".". */
export const RATE_PATTERN = /^\s*\d{1,2}([.,]\d{1,2})?\s*$/;

/** "1,5" → 1.5; empty or malformed → null. */
export function parseRate(value: string): number | null {
	if (!RATE_PATTERN.test(value)) return null;
	return Number(value.trim().replace(",", "."));
}

/** 1.5 → "1,5" for a rate field (parses back unchanged). */
export function rateText(rate: number | null | undefined): string {
	return rate === null || rate === undefined ? "" : String(rate).replace(".", ",");
}

/** True when the typed rate is well formed and within [min, max]. */
export function rateWithin(value: string, min: number, max: number): boolean {
	const rate = parseRate(value);
	return rate !== null && rate >= min && rate <= max;
}

/** Today as YYYY-MM-DD in the local time zone (toISOString would give yesterday after midnight in Poland). */
export function todayIso(now = new Date()): string {
	const month = String(now.getMonth() + 1).padStart(2, "0");
	const day = String(now.getDate()).padStart(2, "0");
	return `${now.getFullYear()}-${month}-${day}`;
}

/** First day of the month of a YYYY-MM-DD date. */
export function firstOfMonth(date: string): string {
	return `${date.slice(0, 7)}-01`;
}

/** First day of the next month — the default start of a new settings period (art. 26 ust. 4). */
export function firstOfNextMonth(date: string): string {
	const year = Number(date.slice(0, 4));
	const month = Number(date.slice(5, 7));
	return month === 12 ? `${year + 1}-01-01` : `${year}-${String(month + 1).padStart(2, "0")}-01`;
}

/** First automatic re-enrolment was in 2023 (art. 23 ust. 5), then every 4 years (R12). */
export const FIRST_CYCLE_YEAR = 2023;

/** The cycle year now or next: 2026 → 2027, 2027 → 2027, 2028 → 2031. */
export function nextCycleYear(year: number): number {
	if (year <= FIRST_CYCLE_YEAR) return FIRST_CYCLE_YEAR;
	return FIRST_CYCLE_YEAR + Math.ceil((year - FIRST_CYCLE_YEAR) / 4) * 4;
}

/** Cycle years offered in the year select: the previous, the current or next and the one after. */
export function cycleYearOptions(year: number, selected: number): number[] {
	const next = nextCycleYear(year);
	return [...new Set([next - 4, next, next + 4, selected])]
		.filter((value) => value >= FIRST_CYCLE_YEAR)
		.sort((a, b) => a - b);
}

/**
 * Re-enrolment list split into its groups in a fixed order, keeping the API order inside a group. Every group is
 * present (possibly empty), so the screen can say "nobody" per group; rows with an unknown group are dropped.
 */
export function groupReenrollment<T extends { group: string }>(
	items: readonly T[],
): Record<ReenrollmentGroup, T[]> {
	const groups: Record<ReenrollmentGroup, T[]> = { restart: [], requestOnly: [], noRestart: [] };
	for (const item of items) {
		if ((REENROLLMENT_GROUPS as readonly string[]).includes(item.group)) {
			groups[item.group as ReenrollmentGroup].push(item);
		}
	}
	return groups;
}

/** Counts of the candidate list by status, for the summary above the table. */
export function countByStatus<T extends { status: string }>(
	items: readonly T[],
): Record<CandidateStatus, number> {
	const counts: Record<CandidateStatus, number> = {
		waiting: 0,
		toEnroll: 0,
		overdue: 0,
		requestOnly: 0,
	};
	for (const item of items) {
		if (item.status in counts) counts[item.status as CandidateStatus] += 1;
	}
	return counts;
}
