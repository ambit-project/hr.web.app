import { createFileRoute, redirect } from "@tanstack/react-router";
import { z } from "zod";
import { getAuthOptionsFn } from "#/features/auth/auth.functions";
import { LoginPage } from "#/features/auth/LoginPage";

export const Route = createFileRoute("/login")({
	validateSearch: z.object({
		// Only paths inside the app: an absolute URL here would make the login page an open redirect.
		redirect: z
			.string()
			.regex(/^\/(?!\/)/)
			.optional()
			.catch(undefined),
	}),
	beforeLoad: ({ context }) => {
		if (context.me) throw redirect({ to: "/" });
	},
	loader: () => getAuthOptionsFn(),
	component: LoginPage,
});
