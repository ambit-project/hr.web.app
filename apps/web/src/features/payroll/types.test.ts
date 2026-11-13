import { describe, expect, it } from "vitest";
import {
	accidentRateFromPercent,
	accidentRatePercentText,
	displayStatus,
	formatLineRate,
	formatPeriod,
	groupLines,
	lastOfMonth,
	latestPaymentDate,
	lineGroup,
	memberChanges,
	monthOptions,
	shortId,
	sumEntries,
	traceRows,
} from "./types";

const line = (seq: number, code: string, side: string, amount: number) => ({
	seq,
	code,
	side,
	amount,
});

describe("payslip groups", () => {
	it("puts each line in its payslip group", () => {
		expect(lineGroup({ code: "ZASAD", side: "income" })).toBe("income");
		expect(lineGroup({ code: "PREMIA", side: "income" })).toBe("income");
		expect(lineGroup({ code: "ZUS-E-P", side: "employee" })).toBe("contributions");
		expect(lineGroup({ code: "ZDROW", side: "employee" })).toBe("contributions");
		expect(lineGroup({ code: "PIT", side: "employee" })).toBe("tax");
		expect(lineGroup({ code: "KUP", side: "info" })).toBe("tax");
		expect(lineGroup({ code: "PPK-P", side: "employee" })).toBe("ppk");
		expect(lineGroup({ code: "PPK-PR", side: "employer" })).toBe("ppk");
		expect(lineGroup({ code: "KOMOR", side: "deduction" })).toBe("deductions");
		expect(lineGroup({ code: "FP", side: "employer" })).toBe("employer");
		expect(lineGroup({ code: "PODST-ZUS", side: "info" })).toBe("info");
		expect(lineGroup({ code: "ZAS-ZUS", side: "info" })).toBe("info");
	});

	it("keeps the group order and the engine's order, drops empty groups and sums what adds up", () => {
		const groups = groupLines([
			line(5, "PIT", "employee", 455),
			line(1, "ZASAD", "income", 7500),
			line(2, "ZASAD-CH", "income", -1250),
			line(4, "PODST-PIT", "info", 6200),
			line(3, "ZUS-E-P", "employee", 610.5),
			line(6, "PPK-P", "employee", 150),
			line(7, "PPK-PR", "employer", 112.5),
			line(8, "PODST-ZUS", "info", 6250),
		]);

		expect(groups.map((group) => group.group)).toEqual([
			"income",
			"contributions",
			"tax",
			"ppk",
			"info",
		]);
		expect(groups[0]?.lines.map((item) => item.code)).toEqual(["ZASAD", "ZASAD-CH"]);
		expect(groups[0]?.total).toBe(6250);
		expect(groups[1]?.total).toBe(610.5);
		// Only the advance counts in the tax group; the tax base is information.
		expect(groups[2]?.lines.map((item) => item.code)).toEqual(["PODST-PIT", "PIT"]);
		expect(groups[2]?.total).toBe(455);
		expect(groups[3]?.total).toBeNull();
		expect(groups[4]?.total).toBeNull();
	});
});

describe("totals", () => {
	it("sums payslip amounts to the cent without floating noise", () => {
		const entry = {
			gross: 0.1,
			employeeContributions: 0.2,
			health: 0,
			pit: 1,
			ppkEmployee: 0,
			net: 0.7,
			deductions: 0,
			payout: 0.7,
			employerCost: 0.05,
		};
		const totals = sumEntries([entry, { ...entry, gross: 0.2 }]);
		expect(totals.gross).toBe(0.3);
		expect(totals.employeeContributions).toBe(0.4);
		expect(totals.pit).toBe(2);
		expect(totals.employerCost).toBe(0.1);
		expect(sumEntries([]).payout).toBe(0);
	});
});

describe("trace", () => {
	it("reads legal parameters with their dates", () => {
		const rows = traceRows({
			params: {
				"zus.emerytalna.pracownik": { value: 0.0976, validFrom: "1999-01-01" },
				"placa.minimalna": { value: 4806, validFrom: "2026-01-01" },
			},
		});
		expect(rows).toEqual([
			{
				id: "params.zus.emerytalna.pracownik",
				key: "param",
				depth: 0,
				value: {
					type: "param",
					code: "zus.emerytalna.pracownik",
					value: "0,0976",
					validFrom: "1999-01-01",
				},
			},
			{
				id: "params.placa.minimalna",
				key: "param",
				depth: 0,
				value: {
					type: "param",
					code: "placa.minimalna",
					value: "4\u00a0806",
					validFrom: "2026-01-01",
				},
			},
		]);
	});

	it("shortens identifiers, nests objects and numbers lists of objects", () => {
		const rows = traceRows({
			absenceIds: ["01a1d0db-7fa1-7264-9124-361aaf9f9287"],
			days: 5,
			rule: "1/30",
			paid: true,
			base: { average: 6083.45, months: [{ month: "2026-10-01", income: 7500 }] },
			termsId: "01a1d0db-7fa1-7264-9124-361aaf9f9288",
		});
		expect(rows.map((row) => [row.key, row.depth, row.value])).toEqual([
			["absenceIds", 0, { type: "ids", ids: ["01a1d0db"] }],
			["days", 0, { type: "text", text: "5" }],
			["rule", 0, { type: "text", text: "1/30" }],
			["paid", 0, { type: "bool", value: true }],
			["base", 0, { type: "group" }],
			["average", 1, { type: "text", text: "6\u00a0083,45" }],
			["months", 1, { type: "group" }],
			["1", 2, { type: "group" }],
			["month", 3, { type: "text", text: "2026-10-01" }],
			["income", 3, { type: "text", text: "7\u00a0500" }],
			["termsId", 0, { type: "text", text: "01a1d0db" }],
		]);
		expect(new Set(rows.map((row) => row.id)).size).toBe(rows.length);
	});

	it("has nothing to show for an empty or malformed trace", () => {
		expect(traceRows({})).toEqual([]);
		expect(traceRows(null)).toEqual([]);
		expect(traceRows("x")).toEqual([]);
	});

	it("shortens only identifiers", () => {
		expect(shortId("01a1d0db-7fa1-7264-9124-361aaf9f9287")).toBe("01a1d0db");
		expect(shortId("2026-11-01")).toBe("2026-11-01");
	});
});

describe("formatting", () => {
	it("shows the calculated-but-stale state on its own", () => {
		expect(displayStatus({ status: "calculated", isStale: true })).toBe("stale");
		expect(displayStatus({ status: "calculated", isStale: false })).toBe("calculated");
		expect(displayStatus({ status: "closed", isStale: false })).toBe("closed");
		expect(displayStatus({ status: "weird", isStale: false })).toBe("open");
	});

	it("formats rates of lines as percentages", () => {
		expect(formatLineRate(0.0976)).toBe("9,76%");
		expect(formatLineRate(0.8)).toBe("80%");
		expect(formatLineRate(0.0245)).toBe("2,45%");
		expect(formatLineRate(null)).toBe("");
	});

	it("converts the accident rate between percent and fraction", () => {
		expect(accidentRateFromPercent("1,67")).toBe(0.0167);
		expect(accidentRateFromPercent("0.4")).toBe(0.004);
		expect(accidentRateFromPercent("abc")).toBeNull();
		expect(accidentRatePercentText(0.0167)).toBe("1,67");
	});

	it("knows month ends and the payment deadline", () => {
		expect(lastOfMonth("2026-02-01")).toBe("2026-02-28");
		expect(lastOfMonth("2026-11-15")).toBe("2026-11-30");
		expect(latestPaymentDate("2026-11-01")).toBe("2026-12-10");
		expect(latestPaymentDate("2026-12-01")).toBe("2027-01-10");
		expect(formatPeriod("2026-11-01")).toBe("11.2026");
		expect(monthOptions(2026)).toHaveLength(12);
		expect(monthOptions(2026)[10]).toBe("2026-11-01");
	});
});

describe("members step", () => {
	it("sends only the changes against the server", () => {
		const members = [
			{ employmentId: "a", excluded: false },
			{ employmentId: "b", excluded: true },
			{ employmentId: "c", excluded: false },
		];
		expect(memberChanges(members, new Set(["a", "b"]))).toEqual({ exclude: ["a"], include: [] });
		expect(memberChanges(members, new Set())).toEqual({ exclude: [], include: ["b"] });
		expect(memberChanges(members, new Set(["b"]))).toEqual({ exclude: [], include: [] });
	});
});
