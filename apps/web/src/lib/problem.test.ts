import { AxiosError, type AxiosResponse } from "axios";
import { describe, expect, it } from "vitest";
import { ProblemError, toResult, unwrap } from "./problem";

function axiosError(status: number, data: unknown) {
	return new AxiosError("failed", "ERR", undefined, undefined, {
		status,
		data,
	} as AxiosResponse);
}

describe("toResult", () => {
	it("keeps field codes from a validation problem", async () => {
		const result = await toResult(() =>
			Promise.reject(axiosError(400, { code: "U0001", errors: { name: ["duplicate"] } })),
		);
		expect(result).toEqual({
			ok: false,
			problem: { status: 400, code: "U0001", errors: { name: ["duplicate"] } },
		});
	});

	it("rethrows server failures", async () => {
		const error = axiosError(500, {});
		await expect(toResult(() => Promise.reject(error))).rejects.toBe(error);
	});

	it("unwrap throws ProblemError for a problem", () => {
		expect(() => unwrap({ ok: false, problem: { status: 404 } })).toThrow(ProblemError);
	});
});
