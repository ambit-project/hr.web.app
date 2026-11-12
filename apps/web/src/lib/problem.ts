import { isAxiosError } from "axios";

/**
 * ProblemDetails from the API: `code` is the database error code (U0001…), `errors` maps fields to codes and `reason`
 * names the cause of a conflict (e.g. `retention`), translated under `reasons.<reason>`.
 */
export type Problem = {
	status: number;
	code?: string;
	errors?: Record<string, string[]>;
	reason?: string;
};

/** Server function result: problems travel as data, because thrown errors lose their shape on the way to the browser. */
export type Result<T> = { ok: true; value: T } | { ok: false; problem: Problem };

/** Turns a 4xx answer with ProblemDetails into a result; anything else is a server failure and is rethrown. */
export async function toResult<T>(call: () => Promise<T>): Promise<Result<T>> {
	try {
		return { ok: true, value: await call() };
	} catch (error) {
		const response = isAxiosError(error) ? error.response : undefined;
		if (response && response.status >= 400 && response.status < 500) {
			const data = (response.data ?? {}) as Partial<Problem>;
			return {
				ok: false,
				problem: {
					status: response.status,
					code: data.code,
					errors: data.errors,
					reason: data.reason,
				},
			};
		}
		throw error;
	}
}

export class ProblemError extends Error {
	constructor(readonly problem: Problem) {
		super(problem.code ?? `HTTP ${problem.status}`);
	}
}

export function unwrap<T>(result: Result<T>): T {
	if (!result.ok) throw new ProblemError(result.problem);
	return result.value;
}
