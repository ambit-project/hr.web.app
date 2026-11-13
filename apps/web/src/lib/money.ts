const AMOUNT = new Intl.NumberFormat("pl-PL", {
	minimumFractionDigits: 2,
	maximumFractionDigits: 2,
	useGrouping: true,
});

/** 1234.5 → "1 234,50" (Polish separators, always two decimals). */
export function formatDecimal(amount: number): string {
	return AMOUNT.format(amount);
}

/** 120 → "120,00 zł". */
export function formatMoney(amount: number): string {
	return `${formatDecimal(amount)} zł`;
}

/** Text a person types as an amount: digits with optional spaces and up to two decimals after "," or ".". */
export const AMOUNT_PATTERN = /^\s*\d[\d\s]*([.,]\d{1,2})?\s*$/;

/** "6 500,50" → 6500.5; empty → null (the database answers "required"). */
export function parseAmount(value: string): number | null {
	const normalized = value.replace(/\s/g, "").replace(",", ".");
	return normalized === "" ? null : Number(normalized);
}

/** 120 → "120,00" for an amount field (comma, no grouping, so it parses back unchanged). */
export function amountText(amount: number | null | undefined): string {
	return amount === null || amount === undefined ? "" : amount.toFixed(2).replace(".", ",");
}
