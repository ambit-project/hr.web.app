import { z } from "zod";

export type ValidationCode = "required" | "maxLength" | "duplicate" | "invalid";

/** Text field rules mirrored from the database routine; the database stays the authority. */
export function text({ required, max }: { required?: boolean; max: number }) {
	const base = z.string().max(max, "maxLength");
	return required ? base.trim().min(1, "required") : base;
}
