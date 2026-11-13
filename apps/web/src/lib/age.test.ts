import { describe, expect, it } from "vitest";
import { ageOn, birthDateWithAge } from "./age";

describe("ageOn", () => {
	it("counts full years and waits for the birthday", () => {
		expect(ageOn("1985-11-13", new Date(2026, 10, 13))).toBe(41);
		expect(ageOn("1985-11-14", new Date(2026, 10, 13))).toBe(40);
	});

	it("is null for a malformed date", () => {
		expect(ageOn("13.11.1985")).toBeNull();
	});
});

describe("birthDateWithAge", () => {
	it("adds the age in brackets", () => {
		expect(birthDateWithAge("2002-08-19", new Date(2026, 10, 13))).toBe("2002-08-19 (24)");
	});
});
