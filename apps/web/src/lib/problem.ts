import { isAxiosError } from "axios";
import type { TFunction } from "i18next";

/**
 * ProblemDetails from the API: `code` is the database error code (U0001…), `errors` maps fields to codes and `reason`
 * names the cause of a conflict (e.g. `retention`), translated under `reasons.<reason>`. `traceId` names the request in
 * the API log, so a person can quote it when reporting a failure (pattern: ambit.es).
 */
export type Problem = {
	status: number;
	code?: string;
	errors?: Record<string, string[]>;
	reason?: string;
	title?: string;
	traceId?: string;
};

/** Server function result: problems travel as data, because thrown errors lose their shape on the way to the browser. */
export type Result<T> = { ok: true; value: T } | { ok: false; problem: Problem };

/**
 * Turns an API error answer (4xx and 5xx with ProblemDetails) into a result, so its status and trace id survive the
 * trip from the server function to the browser. No answer at all (API unreachable) becomes a 503 without a trace id.
 * Anything that is not an HTTP failure is rethrown.
 */
export async function toResult<T>(call: () => Promise<T>): Promise<Result<T>> {
	try {
		return { ok: true, value: await call() };
	} catch (error) {
		if (!isAxiosError(error)) throw error;
		const response = error.response;
		if (!response) return { ok: false, problem: { status: 503 } };
		if (response.status < 400) throw error;
		const data = (response.data ?? {}) as Partial<Problem>;
		return {
			ok: false,
			problem: {
				status: response.status,
				code: data.code,
				errors: data.errors,
				reason: data.reason,
				title: data.title,
				traceId: data.traceId,
			},
		};
	}
}

export class ProblemError extends Error {
	constructor(readonly problem: Problem) {
		super(problem.code ?? `HTTP ${problem.status}`);
	}
}

export function unwrap<T>(result: Result<T>): T {
	if (!result.ok) {
		rememberFailure(result.problem);
		throw new ProblemError(result.problem);
	}
	return result.value;
}

/** How long after a failed call a "server error" message is taken to be about it. */
const RECENT_MS = 5_000;

let lastFailure: { status: number; traceId?: string; at: number } | undefined;

/** Remembers the latest server failure in the browser (the server renders for many people, so it never keeps one). */
function rememberFailure(problem: Problem, now = Date.now()) {
	if (typeof window === "undefined" || problem.status < 500) return;
	lastFailure = { status: problem.status, traceId: problem.traceId, at: now };
}

/** The failure of a moment ago, handed out once; an older one is dropped. */
export function takeRecentFailure(
	now = Date.now(),
): { status: number; traceId?: string } | undefined {
	const failure = lastFailure;
	lastFailure = undefined;
	return failure && now - failure.at <= RECENT_MS ? failure : undefined;
}

/**
 * Message for a failed save: "unavailable" for a 503, the general server error otherwise, plus the trace id to quote
 * when reporting it.
 */
export function serverErrorText(t: TFunction, now = Date.now()): string {
	const failure = takeRecentFailure(now);
	const message = failure?.status === 503 ? t("errors.unavailable") : t("common.serverError");
	return failure?.traceId
		? `${message} ${t("errors.reference", { traceId: failure.traceId })}`
		: message;
}
