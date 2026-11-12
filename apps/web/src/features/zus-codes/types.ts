// Kinds checked by dictionaries.zus_codes (db/changelog/1-ddl/20261112-08-…): title = 4 digits, others = 1 digit.
export const ZUS_CODE_KINDS = ["title", "pension", "disability"] as const;
export type ZusCodeKind = (typeof ZUS_CODE_KINDS)[number];

export const ZUS_CODE_PATTERN: Record<ZusCodeKind, RegExp> = {
	title: /^\d{4}$/,
	pension: /^\d$/,
	disability: /^\d$/,
};

export function isZusCodeKind(value: string): value is ZusCodeKind {
	return (ZUS_CODE_KINDS as readonly string[]).includes(value);
}
