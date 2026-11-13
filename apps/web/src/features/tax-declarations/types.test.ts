import { describe, expect, it } from "vitest";
import { allowedMonths, formatMonth } from "./types";

describe("allowedMonths", () => {
	it("offers the month of submission and the next one", () => {
		expect(allowedMonths("2026-03-20")).toEqual(["2026-03-01", "2026-04-01"]);
	});

	it("rolls over to January of the next year", () => {
		expect(allowedMonths("2026-12-10")).toEqual(["2026-12-01", "2027-01-01"]);
	});

	it("offers nothing without a date", () => {
		expect(allowedMonths("")).toEqual([]);
	});
});

describe("formatMonth", () => {
	it("shows month and year", () => {
		expect(formatMonth("2026-04-01")).toBe("04.2026");
	});
});
