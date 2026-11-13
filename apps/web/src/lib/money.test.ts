import { describe, expect, it } from "vitest";
import { AMOUNT_PATTERN, amountText, formatDecimal, formatMoney, parseAmount } from "./money";

// Intl uses a non-breaking space as the Polish thousands separator.
const normalize = (text: string) => text.replace(/\s/g, " ");

describe("formatMoney", () => {
	it("shows two decimals with a comma and the currency", () => {
		expect(formatMoney(120)).toBe("120,00 zł");
	});

	it("groups thousands", () => {
		expect(normalize(formatMoney(12345.5))).toBe("12 345,50 zł");
		expect(normalize(formatDecimal(1234.5))).toBe("1 234,50");
	});
});

describe("parseAmount", () => {
	it("accepts a comma, a dot and spaces", () => {
		expect(parseAmount("6 500,50")).toBe(6500.5);
		expect(parseAmount("120.00")).toBe(120);
	});

	it("is null when empty", () => {
		expect(parseAmount("  ")).toBeNull();
	});
});

describe("AMOUNT_PATTERN", () => {
	it("allows at most two decimals", () => {
		expect(AMOUNT_PATTERN.test("120,5")).toBe(true);
		expect(AMOUNT_PATTERN.test("120,555")).toBe(false);
		expect(AMOUNT_PATTERN.test("abc")).toBe(false);
	});
});

describe("amountText", () => {
	it("round-trips through parseAmount", () => {
		expect(amountText(120)).toBe("120,00");
		expect(parseAmount(amountText(99.9))).toBe(99.9);
		expect(amountText(null)).toBe("");
	});
});
