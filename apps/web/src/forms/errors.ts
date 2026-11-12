import type { ValidationCode } from "./validation";

const KNOWN: readonly ValidationCode[] = [
	"required",
	"maxLength",
	"duplicate",
	"invalid",
	"beforeCurrent",
	"tooShort",
	"beforeFrom",
	"notFound",
	"inactive",
	"mismatch",
];

/**
 * Form errors are codes, never sentences: zod messages and API field errors (U0001) use the same codes, and
 * the field translates them (validation.<code>).
 */
export function errorCode(errors: ReadonlyArray<unknown>): ValidationCode | "unknown" | undefined {
	const first = errors.find(Boolean);
	if (!first) return undefined;
	const code = typeof first === "string" ? first : (first as { message?: string }).message;
	return KNOWN.includes(code as ValidationCode) ? (code as ValidationCode) : "unknown";
}
