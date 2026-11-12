import { createServerFn } from "@tanstack/react-start";
import { deleteCookie, setCookie } from "@tanstack/react-start/server";
import { isAxiosError } from "axios";
import { authOptions, devLogin, login, logout, me } from "#/api/endpoints/auth/auth";
import type { AuthEndpointsLoginResponse, AuthEndpointsMeResponse } from "#/api/models";
import { apiOptions, SESSION_COOKIE } from "#/server/api";

export type Role = "kadry" | "administrator" | "pracownik";

function storeSession({ token, expiresAt }: AuthEndpointsLoginResponse) {
	setCookie(SESSION_COOKIE, token, {
		httpOnly: true,
		sameSite: "lax",
		secure: process.env.NODE_ENV === "production",
		path: "/",
		expires: new Date(expiresAt),
	});
}

async function startSession(call: () => Promise<AuthEndpointsLoginResponse>) {
	try {
		storeSession(await call());
		return { ok: true as const };
	} catch (error) {
		if (isAxiosError(error) && error.response?.status === 401) return { ok: false as const };
		throw error;
	}
}

/** The signed-in account, or null without a valid session. */
export const getMeFn = createServerFn({ method: "GET" }).handler(
	async (): Promise<AuthEndpointsMeResponse | null> => {
		const options = apiOptions();
		if (!options.headers) return null;
		try {
			return await me(options);
		} catch (error) {
			if (isAxiosError(error) && error.response?.status === 401) {
				deleteCookie(SESSION_COOKIE, { path: "/" });
				return null;
			}
			throw error;
		}
	},
);

export const getAuthOptionsFn = createServerFn({ method: "GET" }).handler(() =>
	authOptions(apiOptions()),
);

export const loginFn = createServerFn({ method: "POST" })
	.validator((input: { login: string; password: string }) => input)
	.handler(({ data }) => startSession(() => login(data, apiOptions())));

export const devLoginFn = createServerFn({ method: "POST" })
	.validator((input: { role: Role }) => input)
	.handler(({ data }) => startSession(() => devLogin(data, apiOptions())));

export const logoutFn = createServerFn({ method: "POST" }).handler(async () => {
	const options = apiOptions();
	if (options.headers) {
		await logout(options).catch(() => undefined);
	}
	deleteCookie(SESSION_COOKIE, { path: "/" });
});
