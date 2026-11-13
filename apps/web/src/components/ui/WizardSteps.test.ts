import { describe, expect, it } from "vitest";
import { firstStepWithError } from "./WizardSteps";

describe("firstStepWithError", () => {
	const steps = [
		["name", "pesel"],
		["street", "city"],
	] as const;

	it("points at the earliest step with a failed field", () => {
		expect(firstStepWithError(steps, { city: ["required"], pesel: ["invalid"] })).toBe(0);
		expect(firstStepWithError(steps, { city: ["required"] })).toBe(1);
	});

	it("is null when no step owns the failed field", () => {
		expect(firstStepWithError(steps, { other: ["x"] })).toBeNull();
		expect(firstStepWithError(steps, undefined)).toBeNull();
	});
});
