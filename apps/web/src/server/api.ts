import { redirect } from "@tanstack/react-router";
import { getCookie } from "@tanstack/react-start/server";
import { type AxiosRequestConfig, isAxiosError } from "axios";

// Server-only: the browser never calls the API directly. Server functions call it with the session token taken
// from the httpOnly cookie, so the token never reaches JavaScript in the browser.
const API_URL = process.env.HR_API_URL ?? "http://localhost:6300";
// Without a limit a call to an API that is down hangs the save forever; after it the person sees "unavailable"
// (toResult turns a call without an answer into a 503).
const API_TIMEOUT_MS = Number(process.env.HR_API_TIMEOUT_MS ?? 30_000);

export const SESSION_COOKIE = "hr_session";

/** Options for generated API functions called inside server functions. */
export function apiOptions(): AxiosRequestConfig {
	const token = getCookie(SESSION_COOKIE);
	return {
		baseURL: API_URL,
		timeout: API_TIMEOUT_MS,
		headers: token ? { Authorization: `Bearer ${token}` } : undefined,
	};
}

/** Calls the API; a 401 (no or expired session) sends the person to the login page. */
export async function callApi<T>(call: () => Promise<T>): Promise<T> {
	try {
		return await call();
	} catch (error) {
		if (isAxiosError(error) && error.response?.status === 401) {
			throw redirect({ to: "/login" });
		}
		throw error;
	}
}

/** Options for an API call that returns a file (xls, pdf): the body comes as bytes. */
export function fileApiOptions(): AxiosRequestConfig {
	return { ...apiOptions(), responseType: "arraybuffer" };
}

/**
 * Calls an API endpoint that returns a file and gives it back as base64 (a server function returns JSON). A failure
 * body arrives as bytes too, so it is parsed back to the ProblemDetails toResult reads.
 */
export async function callApiFile(call: () => Promise<unknown>): Promise<string> {
	try {
		const bytes = await callApi(call);
		return Buffer.from(bytes as ArrayBuffer).toString("base64");
	} catch (error) {
		if (isAxiosError(error) && error.response?.data instanceof ArrayBuffer) {
			const text = Buffer.from(error.response.data).toString("utf8");
			try {
				error.response.data = JSON.parse(text);
			} catch {
				error.response.data = {};
			}
		}
		throw error;
	}
}
