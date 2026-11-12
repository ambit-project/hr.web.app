import { describe, expect, it } from "vitest";
import { changes, formatValue } from "./changes";

describe("changes", () => {
	it("lists only changed fields, without bookkeeping columns", () => {
		expect(
			changes(
				JSON.stringify({ id: "1", name: "A", city: "X", created_at: "t1" }),
				JSON.stringify({ id: "1", name: "B", city: "X", created_at: "t2" }),
			),
		).toEqual([{ field: "name", before: "A", after: "B" }]);
	});

	it("treats a create as all fields set", () => {
		expect(changes(null, JSON.stringify({ name: "A", roles: ["kadry"] }))).toEqual([
			{ field: "name", before: undefined, after: "A" },
			{ field: "roles", before: undefined, after: ["kadry"] },
		]);
	});

	it("formats values for display", () => {
		expect(formatValue(["kadry", "administrator"])).toBe("kadry, administrator");
		expect(formatValue(null)).toBe("—");
	});
});
