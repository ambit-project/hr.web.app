import { z } from "zod";

export type ValidationCode =
	| "required"
	| "maxLength"
	| "duplicate"
	| "invalid"
	| "beforeCurrent"
	| "tooShort"
	| "noDigit"
	| "beforeFrom"
	| "notFound"
	| "inactive"
	| "mismatch"
	| "afterStart"
	| "overlap"
	| "beforeTermination"
	| "termsOutsidePeriod"
	| "outsidePeriod"
	| "missingMandatory"
	| "belowMinimum"
	| "beforeStart"
	| "afterEnd"
	| "future"
	| "outsideAllowed"
	| "beforeSubmission"
	| "tooMany"
	| "consentMissing"
	| "aboveConsent"
	| "afterStatus"
	| "limitExceeded"
	| "leaveLimitExceeded"
	| "notEmployed"
	| "tooEarly"
	| "tooYoung"
	| "tooOld"
	| "requestMissing"
	| "notCycleYear"
	| "noPayments";

/** Text field rules mirrored from the database routine; the database stays the authority. */
export function text({ required, max }: { required?: boolean; max: number }) {
	const base = z.string().max(max, "maxLength");
	return required ? base.trim().min(1, "required") : base;
}
