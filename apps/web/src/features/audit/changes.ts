export type Change = { field: string; before: unknown; after: unknown };

type Json = Record<string, unknown>;

function asObject(value: unknown): Json {
	return value !== null && typeof value === "object" && !Array.isArray(value)
		? (value as Json)
		: {};
}

// Columns kept by the database for its own bookkeeping; they change with every write and say nothing to a person.
const HIDDEN = new Set(["id", "created_at", "created_by", "password_hash"]);

function parse(json: string | null | undefined): Json {
	if (!json) return {};
	try {
		return asObject(JSON.parse(json));
	} catch {
		return {};
	}
}

/** Fields that differ between the state before and after (create: all fields set, delete: all removed). */
export function changes(
	before: string | null | undefined,
	after: string | null | undefined,
): Change[] {
	const left = parse(before);
	const right = parse(after);
	const fields = [...new Set([...Object.keys(left), ...Object.keys(right)])].filter(
		(field) => !HIDDEN.has(field),
	);
	return fields
		.filter((field) => JSON.stringify(left[field]) !== JSON.stringify(right[field]))
		.sort()
		.map((field) => ({ field, before: left[field], after: right[field] }));
}

export function formatValue(value: unknown): string {
	if (value === undefined || value === null) return "—";
	if (Array.isArray(value)) return value.join(", ");
	if (typeof value === "object") return JSON.stringify(value);
	return String(value);
}
