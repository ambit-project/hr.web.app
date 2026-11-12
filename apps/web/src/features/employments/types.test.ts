import { describe, expect, it } from "vitest";
import { emptyTerms, parseAmount, termsRequest } from "./types";

describe("termsRequest", () => {
	it("sends no position, FTE, codes or insurances for a specific-work contract", () => {
		const values = { ...emptyTerms("employment"), positionId: "p1", baseAmount: "2000" };

		const request = termsRequest("specific_work", values);

		expect(request).toMatchObject({
			positionId: null,
			fteNumerator: null,
			zusTitleCode: null,
			insurances: [],
			baseAmount: 2000,
		});
	});

	it("keeps employment defaults: full time, code 0110 and mandatory insurances", () => {
		const request = termsRequest("employment", { ...emptyTerms("employment"), positionId: "p1" });

		expect(request).toMatchObject({ fteNumerator: 1, fteDenominator: 1, zusTitleCode: "0110" });
		expect(request.insurances).toEqual(
			expect.arrayContaining(["pension", "disability", "sickness", "accident", "health"]),
		);
	});
});

describe("parseAmount", () => {
	it.each([
		["6 500,50", 6500.5],
		["40", 40],
		["", null],
	])("%s → %s", (input, expected) => {
		expect(parseAmount(input)).toBe(expected);
	});
});
