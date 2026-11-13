import { describe, expect, it } from "vitest";
import {
	isWholePosition,
	PIT11_PART_E,
	pit11Deadlines,
	pit11State,
	positionValue,
	sumWageCard,
	todayIso,
	xmlFileName,
} from "./types";

describe("pit11State", () => {
	const base = { taxFormId: "f", missing: [], sentToOfficeAt: null, givenToEmployeeAt: null };

	it("reads the furthest step of a form", () => {
		expect(pit11State({ ...base, taxFormId: null })).toBe("notGenerated");
		expect(pit11State({ ...base, missing: ["pesel"] })).toBe("missingData");
		expect(pit11State(base)).toBe("generated");
		expect(pit11State({ ...base, givenToEmployeeAt: "2027-02-01" })).toBe("given");
		expect(
			pit11State({ ...base, givenToEmployeeAt: "2027-02-01", sentToOfficeAt: "2027-01-20" }),
		).toBe("sent");
	});
});

describe("pit11Deadlines", () => {
	it("ends January and February of the next year, with a leap February", () => {
		expect(pit11Deadlines(2026)).toEqual({ office: "2027-01-31", taxpayer: "2027-02-28" });
		expect(pit11Deadlines(2027)).toEqual({ office: "2028-01-31", taxpayer: "2028-02-29" });
	});
});

describe("part E layout", () => {
	it("follows the XSD numbers and keeps advances whole", () => {
		expect(PIT11_PART_E).toHaveLength(15);
		expect(PIT11_PART_E[0]?.cells).toEqual([29, 30, 31, 32, 33]);
		expect(PIT11_PART_E[4]?.cells).toEqual([54, 55, 56, null, 57]);
		expect(PIT11_PART_E.every((row) => isWholePosition(row.cells[4] ?? 0))).toBe(true);
		expect(isWholePosition(29)).toBe(false);
		expect(positionValue({ P_29: 7500 }, 29)).toBe(7500);
		expect(positionValue({ P_29: 7500 }, 30)).toBeUndefined();
	});
});

describe("sumWageCard", () => {
	it("adds every amount column to the grosz", () => {
		const row = {
			gross: 0.1,
			zusBase: 0.2,
			employeeContributions: 1,
			employerContributions: 2,
			health: 3,
			costs: 4,
			pit: 5,
			ppkEmployee: 6,
			ppkEmployer: 7,
			deductions: 8,
			net: 9,
			payout: 10,
		};
		const totals = sumWageCard([row, row, row]);
		expect(totals.gross).toBe(0.3);
		expect(totals.zusBase).toBe(0.6);
		expect(totals.payout).toBe(30);
	});
});

describe("small helpers", () => {
	it("formats today and the XML file name", () => {
		expect(todayIso(new Date(2026, 10, 3))).toBe("2026-11-03");
		expect(xmlFileName({ year: 2026, employeeNumber: "P-001", version: 2 })).toBe(
			"PIT-11_2026_P-001_v2.xml",
		);
	});
});
