import { describe, expect, it } from "vitest";
import { resolveDark } from "./theme";

describe("resolveDark", () => {
	it("follows the system only for the system preference", () => {
		expect(resolveDark("system", true)).toBe(true);
		expect(resolveDark("system", false)).toBe(false);
		expect(resolveDark("light", true)).toBe(false);
		expect(resolveDark("dark", false)).toBe(true);
	});
});
