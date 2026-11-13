import { describe, expect, it } from "vitest";
import { nextTheme } from "./theme";

describe("nextTheme", () => {
	it("switches between light and dark", () => {
		expect(nextTheme("light")).toBe("dark");
		expect(nextTheme("dark")).toBe("light");
	});
});
