import { describe, expect, it } from "vitest";
import type { ListComponentTypesItem, ListConsentsItem } from "#/api/models";
import {
	assignableTypes,
	authorityKindsFor,
	calculationsFor,
	formatComponentValue,
	formatPeriod,
	garnishmentRequest,
	requiresConsent,
	setFlags,
	signedConsentsFor,
} from "./types";

function type(overrides: Partial<ListComponentTypesItem>): ListComponentTypesItem {
	return {
		id: "t1",
		code: "PREMIA",
		name: "Premia",
		kind: "addition",
		calculation: "fixed",
		inZusBase: true,
		inHealthBase: true,
		taxable: true,
		inSicknessBase: true,
		deductionCategory: null,
		requiresConsent: false,
		isSystem: false,
		isActive: true,
		isUsed: false,
		rowVersion: 1,
		...overrides,
	};
}

function consent(overrides: Partial<ListConsentsItem>): ListConsentsItem {
	return {
		id: "c1",
		componentTypeId: "t1",
		code: "KARTA",
		name: "Karta",
		monthlyAmount: 120,
		validFrom: "2026-01-01",
		validTo: null,
		signedAt: "2025-12-20",
		status: "active",
		isUsed: false,
		rowVersion: 1,
		...overrides,
	};
}

describe("calculationsFor", () => {
	it("offers only a fixed amount for a deduction", () => {
		expect(calculationsFor("deduction")).toEqual(["fixed"]);
	});

	it("never offers the payroll engine", () => {
		expect(calculationsFor("addition")).not.toContain("engine");
	});
});

describe("requiresConsent", () => {
	it("is true for voluntary deductions only", () => {
		expect(requiresConsent("voluntaryOther")).toBe(true);
		expect(requiresConsent("voluntaryEmployer")).toBe(true);
		expect(requiresConsent("advance")).toBe(false);
		expect(requiresConsent(null)).toBe(false);
	});
});

describe("setFlags", () => {
	it("lists the flags that are on, in order", () => {
		expect(setFlags(type({ inHealthBase: false, inSicknessBase: null }))).toEqual([
			"inZusBase",
			"taxable",
		]);
	});
});

describe("authorityKindsFor", () => {
	it("allows a creditor only for alimony", () => {
		expect(authorityKindsFor("alimony")).toContain("creditor");
		expect(authorityKindsFor("other")).not.toContain("creditor");
	});
});

describe("assignableTypes", () => {
	const types = [
		type({ id: "a" }),
		type({ id: "inactive", isActive: false }),
		type({ id: "system", isSystem: true }),
		type({ id: "penalty", kind: "deduction", deductionCategory: "penalty" }),
	];

	it("keeps active, non-system types", () => {
		expect(assignableTypes(types, "employment").map((item) => item.id)).toEqual(["a", "penalty"]);
	});

	it("drops a penalty outside an employment contract", () => {
		expect(assignableTypes(types, "mandate").map((item) => item.id)).toEqual(["a"]);
	});
});

describe("signedConsentsFor", () => {
	it("keeps signed consents of the type", () => {
		const consents = [
			consent({ id: "ok" }),
			consent({ id: "unsigned", signedAt: null }),
			consent({ id: "other", componentTypeId: "t2" }),
		];
		expect(signedConsentsFor(consents, "t1").map((item) => item.id)).toEqual(["ok"]);
	});
});

describe("formatComponentValue", () => {
	it("formats by the way of calculation", () => {
		expect(formatComponentValue("fixed", 120)).toBe("120,00 zł");
		expect(formatComponentValue("percentOfBase", 10)).toBe("10,00%");
		expect(formatComponentValue("rateTimesHours", 25.5)).toBe("25,50 zł/godz.");
	});
});

describe("formatPeriod", () => {
	it("shows an open end as an ellipsis", () => {
		expect(formatPeriod("2026-01-01", null)).toBe("2026-01-01 – …");
		expect(formatPeriod("2026-01-01", "2026-06-30")).toBe("2026-01-01 – 2026-06-30");
	});
});

describe("garnishmentRequest", () => {
	it("trims texts, parses amounts and sends blanks as null", () => {
		expect(
			garnishmentRequest({
				kind: "alimony",
				authorityKind: "creditor",
				authorityName: "  Jan Kowalski ",
				caseNumber: "I Nc 1/26",
				receivedAt: "2026-03-02",
				monthlyAmount: "800,00",
				totalAmount: "",
				note: " ",
			}),
		).toEqual({
			kind: "alimony",
			authorityKind: "creditor",
			authorityName: "Jan Kowalski",
			caseNumber: "I Nc 1/26",
			receivedAt: "2026-03-02",
			monthlyAmount: 800,
			totalAmount: null,
			note: null,
		});
	});
});
