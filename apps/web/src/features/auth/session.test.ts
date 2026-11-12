import { describe, expect, it } from "vitest";
import { hasRole, homePath, type Me } from "./session";

const me = (roles: string[]): Me => ({
	id: "1",
	login: "x",
	email: "x@x.example",
	displayName: "X",
	roles,
	profiles: [],
});

describe("session", () => {
	it("checks roles", () => {
		expect(hasRole(me(["kadry"]), "kadry", "administrator")).toBe(true);
		expect(hasRole(me(["pracownik"]), "kadry")).toBe(false);
		expect(hasRole(null, "kadry")).toBe(false);
	});

	it("sends employees to their documents and staff to positions", () => {
		expect(homePath(me(["pracownik"]))).toBe("/my");
		expect(homePath(me(["administrator"]))).toBe("/employees");
		expect(homePath(me([]))).toBeNull();
	});
});
