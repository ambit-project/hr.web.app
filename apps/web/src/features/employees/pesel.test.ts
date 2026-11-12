import { describe, expect, it } from "vitest";
import { peselBirthDate } from "./pesel";

describe("peselBirthDate", () => {
	it("reads a 20th-century date", () => {
		expect(peselBirthDate("44051401359")).toBe("1944-05-14");
	});

	it("reads a 21st-century date (month + 20) and ignores separators", () => {
		expect(peselBirthDate("02 27 08 0362 4")).toBe("2002-07-08");
	});

	it("rejects a wrong checksum, a non-existent date and a short number", () => {
		expect(peselBirthDate("44051401358")).toBeNull();
		expect(peselBirthDate("44133101350")).toBeNull();
		expect(peselBirthDate("4405140135")).toBeNull();
	});
});
