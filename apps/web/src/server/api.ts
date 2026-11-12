import type { AxiosRequestConfig } from "axios";

// Server-only: the browser never calls the API directly, it calls server functions which call the API.
const API_URL = process.env.HR_API_URL ?? "http://localhost:6300";

/** Options for generated API functions called inside server functions. */
export function apiOptions(): AxiosRequestConfig {
	return { baseURL: API_URL };
}
