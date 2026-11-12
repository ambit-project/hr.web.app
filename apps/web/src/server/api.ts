import { redirect } from "@tanstack/react-router";
import { getCookie } from "@tanstack/react-start/server";
import { type AxiosRequestConfig, isAxiosError } from "axios";

// Server-only: the browser never calls the API directly. Server functions call it with the session token taken
// from the httpOnly cookie, so the token never reaches JavaScript in the browser.
const API_URL = process.env.HR_API_URL ?? "http://localhost:6300";

export const SESSION_COOKIE = "hr_session";

/** Options for generated API functions called inside server functions. */
export function apiOptions(): AxiosRequestConfig {
	const token = getCookie(SESSION_COOKIE);
	return {
		baseURL: API_URL,
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
