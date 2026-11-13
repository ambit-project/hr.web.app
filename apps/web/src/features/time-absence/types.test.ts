import { describe, expect, it } from "vitest";
import type { ListAbsenceTypesItem } from "#/api/models";
import {
	allowedAbsenceTypes,
	currentYear,
	dayKindsFor,
	defaultYear,
	formatHours,
	formatNumber,
	hoursText,
	hoursToDays,
	isoWeekday,
	monthName,
	monthWeeks,
	parseHours,
	remainingFrom,
	timeContract,
	yearOptions,
} from "./types";

const type = (code: string, sickness: boolean): ListAbsenceTypesItem => ({
	code,
	name: code,
	payment: "paid",
	limitKind: "none",
	limitDays: null,
	sickness,
	legalBasis: null,
});

describe("monthWeeks", () => {
	it("starts November 2026 on Sunday, so six empty places come first", () => {
		const weeks = monthWeeks(2026, 11, [{ date: "2026-11-11", kind: "holiday" }]);
		expect(weeks[0].slice(0, 6)).toEqual([null, null, null, null, null, null]);
		expect(weeks[0][6]?.date).toBe("2026-11-01");
		expect(weeks.flat().filter(Boolean)).toHaveLength(30);
		expect(weeks.every((week) => week.length === 7)).toBe(true);
		const eleventh = weeks.flat().find((cell) => cell?.day === 11);
		expect(eleventh?.data?.kind).toBe("holiday");
		expect(weeks.flat().find((cell) => cell?.day === 12)?.data).toBeUndefined();
	});

	it("counts February in a leap year and fills the last week", () => {
		const weeks = monthWeeks(2028, 2, []);
		expect(weeks.flat().filter(Boolean)).toHaveLength(29);
		expect(weeks.at(-1)?.length).toBe(7);
	});

	it("needs no empty place for a month starting on Monday", () => {
		expect(monthWeeks(2026, 6, [])[0][0]?.date).toBe("2026-06-01");
	});
});

describe("days and kinds", () => {
	it("knows the weekday", () => {
		expect(isoWeekday("2026-11-09")).toBe(1);
		expect(isoWeekday("2026-08-15")).toBe(6);
		expect(isoWeekday("2026-11-01")).toBe(7);
	});

	it("keeps a holiday a holiday and a designated day off on weekdays only", () => {
		expect(dayKindsFor("2026-08-15", true)).toEqual(["holiday"]);
		expect(dayKindsFor("2026-08-14", false)).toContain("designatedOff");
		expect(dayKindsFor("2026-08-16", false)).toEqual(["workday", "dayOff"]);
	});
});

describe("hours and days", () => {
	it("formats Polish numbers", () => {
		expect(formatNumber(7.5)).toBe("7,5");
		expect(formatHours(13.333)).toBe("13,33 h");
		expect(formatHours(160)).toBe("160 h");
	});

	it("turns hours into leave days", () => {
		expect(hoursToDays(208)).toBe(26);
		expect(hoursToDays(100)).toBe(12.5);
	});

	it("parses typed hours", () => {
		expect(parseHours("7,5")).toBe(7.5);
		expect(parseHours(" 8 ")).toBe(8);
		expect(parseHours("")).toBeNull();
		expect(hoursText(7.5)).toBe("7,5");
		expect(hoursText(null)).toBe("");
	});
});

describe("remainingFrom", () => {
	it("reads the days left with limitExceeded", () => {
		expect(remainingFrom({ dateTo: ["limitExceeded"], remainingDays: ["2"] })).toEqual({
			unit: "days",
			value: 2,
		});
	});

	it("reads the hours left with leaveLimitExceeded", () => {
		expect(remainingFrom({ dateTo: ["leaveLimitExceeded"], remainingHours: ["12.5"] })).toEqual({
			unit: "hours",
			value: 12.5,
		});
	});

	it("is empty without a value", () => {
		expect(remainingFrom({ dateTo: ["overlap"] })).toBeNull();
		expect(remainingFrom(undefined)).toBeNull();
	});
});

describe("allowedAbsenceTypes", () => {
	const types = [type("UW", false), type("CH", true), type("CHW", true)];

	it("gives an employment contract everything", () => {
		expect(allowedAbsenceTypes(types, "employment", false)).toHaveLength(3);
	});

	it("gives a mandate sickness only with sickness insurance", () => {
		expect(allowedAbsenceTypes(types, "mandate", true).map((t) => t.code)).toEqual(["CH", "CHW"]);
		expect(allowedAbsenceTypes(types, "mandate", false)).toEqual([]);
	});

	it("gives specific work nothing", () => {
		expect(allowedAbsenceTypes(types, "specific_work", true)).toEqual([]);
	});
});

describe("labels", () => {
	it("names months in Polish with a capital letter", () => {
		expect(monthName(1)).toBe("Styczeń");
		expect(monthName(12)).toBe("Grudzień");
	});

	it("offers years around the current one and the chosen one", () => {
		expect(yearOptions(2026, 2026)).toEqual([2024, 2025, 2026, 2027]);
		expect(yearOptions(2026, 2020)).toEqual([2020, 2024, 2025, 2026, 2027]);
	});

	it("takes the year in the Polish time zone", () => {
		expect(currentYear(new Date("2026-12-31T23:30:00Z"))).toBe(2027);
	});
});

describe("contract", () => {
	it("knows sickness insurance and hourly pay from any terms version", () => {
		expect(
			timeContract({
				id: "c1",
				kind: "mandate",
				termsHistory: [
					{ insurances: ["health"], payBasis: "lumpSum" },
					{ insurances: ["sickness", "health"], payBasis: "hourly" },
				],
			}),
		).toEqual({ id: "c1", kind: "mandate", hasSicknessInsurance: true, hourly: true });
	});

	it("starts with the current year moved into the contract period", () => {
		const open = { startDate: "2020-03-01", endDate: null, terminatedAt: null };
		expect(defaultYear(open, 2026)).toBe(2026);
		expect(defaultYear({ ...open, endDate: "2024-12-31" }, 2026)).toBe(2024);
		expect(defaultYear({ ...open, endDate: "2028-12-31", terminatedAt: "2025-05-31" }, 2026)).toBe(
			2025,
		);
		expect(defaultYear({ ...open, startDate: "2027-01-01" }, 2026)).toBe(2027);
	});
});
