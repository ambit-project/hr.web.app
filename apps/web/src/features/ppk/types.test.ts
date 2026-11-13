import { describe, expect, it } from "vitest";
import {
	CANDIDATE_COLORS,
	countByStatus,
	cycleYearOptions,
	firstOfMonth,
	firstOfNextMonth,
	formatRate,
	groupReenrollment,
	kindHasRate,
	nextCycleYear,
	PARTICIPATION_COLORS,
	parseRate,
	rateText,
	rateWithin,
	statusColor,
	todayIso,
} from "./types";

describe("rates", () => {
	it("formats percentages the Polish way", () => {
		expect(formatRate(2)).toBe("2%");
		expect(formatRate(1.5)).toBe("1,5%");
		expect(formatRate(0.75)).toBe("0,75%");
		expect(formatRate(0)).toBe("0%");
		expect(formatRate(null)).toBe("—");
	});

	it("parses typed rates with a comma or a dot", () => {
		expect(parseRate("1,5")).toBe(1.5);
		expect(parseRate(" 2.25 ")).toBe(2.25);
		expect(parseRate("0")).toBe(0);
		expect(parseRate("")).toBeNull();
		expect(parseRate("1,555")).toBeNull();
		expect(parseRate("abc")).toBeNull();
	});

	it("writes a rate back so it parses unchanged", () => {
		expect(rateText(1.5)).toBe("1,5");
		expect(parseRate(rateText(0.75))).toBe(0.75);
		expect(rateText(null)).toBe("");
	});

	it("checks the limits of the act", () => {
		expect(rateWithin("0,5", 0.5, 2)).toBe(true);
		expect(rateWithin("0,4", 0.5, 2)).toBe(false);
		expect(rateWithin("2,5", 0, 2.5)).toBe(true);
		expect(rateWithin("2,51", 0, 2.5)).toBe(false);
		expect(rateWithin("", 0, 2)).toBe(false);
	});

	it("asks for a rate only on rate declarations", () => {
		expect(kindHasRate("reducedRate")).toBe(true);
		expect(kindHasRate("additionalRate")).toBe(true);
		expect(kindHasRate("resignation")).toBe(false);
		expect(kindHasRate("enrollmentRequest")).toBe(false);
	});
});

describe("status chips", () => {
	it("maps participation and candidate statuses to colours", () => {
		expect(statusColor(PARTICIPATION_COLORS, "active")).toBe("success");
		expect(statusColor(PARTICIPATION_COLORS, "resigned")).toBe("warning");
		expect(statusColor(PARTICIPATION_COLORS, "notEnrolled")).toBe("default");
		expect(statusColor(CANDIDATE_COLORS, "overdue")).toBe("danger");
		expect(statusColor(CANDIDATE_COLORS, "requestOnly")).toBe("warning");
		expect(statusColor(CANDIDATE_COLORS, "toEnroll")).toBe("accent");
		expect(statusColor(CANDIDATE_COLORS, "waiting")).toBe("default");
	});

	it("reads an unknown status as neutral", () => {
		expect(statusColor(CANDIDATE_COLORS, "somethingNew")).toBe("default");
	});

	it("counts candidates by status", () => {
		expect(
			countByStatus([
				{ status: "overdue" },
				{ status: "toEnroll" },
				{ status: "overdue" },
				{ status: "other" },
			]),
		).toEqual({ waiting: 0, toEnroll: 1, overdue: 2, requestOnly: 0 });
	});
});

describe("re-enrolment", () => {
	it("splits the list into groups in a fixed order and keeps the order inside", () => {
		const groups = groupReenrollment([
			{ id: "a", group: "requestOnly" },
			{ id: "b", group: "restart" },
			{ id: "c", group: "restart" },
			{ id: "d", group: "noRestart" },
			{ id: "e", group: "unknown" },
		]);
		expect(Object.keys(groups)).toEqual(["restart", "requestOnly", "noRestart"]);
		expect(groups.restart.map((item) => item.id)).toEqual(["b", "c"]);
		expect(groups.requestOnly.map((item) => item.id)).toEqual(["a"]);
		expect(groups.noRestart.map((item) => item.id)).toEqual(["d"]);
	});

	it("keeps empty groups", () => {
		expect(groupReenrollment([])).toEqual({ restart: [], requestOnly: [], noRestart: [] });
	});

	it("finds the cycle year every 4 years from 2023", () => {
		expect(nextCycleYear(2026)).toBe(2027);
		expect(nextCycleYear(2027)).toBe(2027);
		expect(nextCycleYear(2028)).toBe(2031);
		expect(nextCycleYear(2020)).toBe(2023);
		expect(cycleYearOptions(2026, 2027)).toEqual([2023, 2027, 2031]);
	});
});

describe("dates", () => {
	it("gives today in local time", () => {
		expect(todayIso(new Date(2026, 0, 5, 0, 30))).toBe("2026-01-05");
	});

	it("finds the first day of this and the next month", () => {
		expect(firstOfMonth("2026-11-13")).toBe("2026-11-01");
		expect(firstOfNextMonth("2026-11-13")).toBe("2026-12-01");
		expect(firstOfNextMonth("2026-12-31")).toBe("2027-01-01");
	});
});
