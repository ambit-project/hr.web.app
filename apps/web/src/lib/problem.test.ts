import { AxiosError, type AxiosResponse } from "axios";
import type { TFunction } from "i18next";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ProblemError, serverErrorText, toResult, unwrap } from "./problem";

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

	it("keeps status and trace id of a server failure", async () => {
		const result = await toResult(() =>
			Promise.reject(axiosError(500, { title: "Internal server error", traceId: "abc123" })),
		);
		expect(result).toEqual({
			ok: false,
			problem: { status: 500, title: "Internal server error", traceId: "abc123" },
		});
	});

	it("treats an API that does not answer as unavailable", async () => {
		const result = await toResult(() =>
			Promise.reject(new AxiosError("Network Error", "ERR_NETWORK")),
		);
		expect(result).toEqual({ ok: false, problem: { status: 503 } });
	});

	it("rethrows what is not an HTTP failure", async () => {
		const error = new Error("bug");
		await expect(toResult(() => Promise.reject(error))).rejects.toBe(error);
	});

	it("unwrap throws ProblemError for a problem", () => {
		expect(() => unwrap({ ok: false, problem: { status: 404 } })).toThrow(ProblemError);
	});
});

describe("serverErrorText", () => {
	const t = ((key: string, options?: object) =>
		options ? `${key} ${JSON.stringify(options)}` : key) as unknown as TFunction;

	afterEach(() => {
		vi.unstubAllGlobals();
	});

	it("adds the trace id of a failure a moment ago, once", () => {
		vi.stubGlobal("window", {});
		expect(() => unwrap({ ok: false, problem: { status: 500, traceId: "abc123" } })).toThrow();

		expect(serverErrorText(t)).toBe('common.serverError errors.reference {"traceId":"abc123"}');
		expect(serverErrorText(t)).toBe("common.serverError");
	});

	it("says unavailable for a 503", () => {
		vi.stubGlobal("window", {});
		expect(() => unwrap({ ok: false, problem: { status: 503 } })).toThrow();

		expect(serverErrorText(t)).toBe("errors.unavailable");
	});

	it("forgets an old failure", () => {
		vi.stubGlobal("window", {});
		expect(() => unwrap({ ok: false, problem: { status: 500, traceId: "old" } })).toThrow();

		expect(serverErrorText(t, Date.now() + 10_000)).toBe("common.serverError");
	});
});
