import { Toast } from "@heroui/react";
import type { QueryClient } from "@tanstack/react-query";
import {
	createRootRouteWithContext,
	HeadContent,
	type NavigateOptions,
	Outlet,
	redirect,
	Scripts,
	type ToOptions,
	useRouter,
	useRouterState,
} from "@tanstack/react-router";
import { TriangleAlert } from "lucide-react";
import type { ReactNode } from "react";
import { RouterProvider } from "react-aria-components";
import { useTranslation } from "react-i18next";
import { AppLayout } from "#/components/layout/AppLayout";
import { getAuthOptionsFn, getMeFn } from "#/features/auth/auth.functions";
import type { Me } from "#/features/auth/session";
import { THEME_INIT_SCRIPT } from "#/features/theme/theme";
import { DEFAULT_LANGUAGE, i18next } from "#/i18n";
import appCss from "#/styles/app.css?url";

// HeroUI links with href navigate through TanStack Router instead of reloading the page.
declare module "react-aria-components" {
	interface RouterConfig {
		href: ToOptions["to"];
		routerOptions: Omit<NavigateOptions, keyof ToOptions>;
	}
}

export const Route = createRootRouteWithContext<{ queryClient: QueryClient; me?: Me | null }>()({
	// Every page needs the account: without a session everything except the login page leads to it.
	beforeLoad: async ({ location, context }) => {
		const [me, options] = await Promise.all([
			getMeFn(),
			// The API's dev-login switch is also the dev-environment flag; it does not change while the app runs.
			context.queryClient.query({
				queryKey: ["auth-options"],
				queryFn: () => getAuthOptionsFn(),
				staleTime: "static",
			}),
		]);
		if (!me && location.pathname !== "/login") {
			throw redirect({ to: "/login", search: { redirect: location.href } });
		}
		return { me, isDevEnvironment: options.devLogin };
	},
	head: () => ({
		meta: [
			{ charSet: "utf-8" },
			{ name: "viewport", content: "width=device-width, initial-scale=1" },
			{ title: i18next.t("app.name") },
		],
		links: [{ rel: "stylesheet", href: appCss }],
	}),
	shellComponent: RootDocument,
	component: RootComponent,
});

function RootDocument({ children }: { children: ReactNode }) {
	return (
		// The theme script changes <html> before React hydrates, so its class and data-theme differ on purpose.
		<html lang={DEFAULT_LANGUAGE} suppressHydrationWarning>
			<head>
				{/* biome-ignore lint/security/noDangerouslySetInnerHtml: a constant script that sets the theme before the first paint */}
				<script dangerouslySetInnerHTML={{ __html: THEME_INIT_SCRIPT }} />
				<HeadContent />
			</head>
			<body className="bg-background text-foreground antialiased">
				{children}
				<Toast.Provider placement="bottom end" />
				<Scripts />
			</body>
		</html>
	);
}

function RootComponent() {
	const router = useRouter();
	const { me, isDevEnvironment } = Route.useRouteContext();
	const isLogin = useRouterState({ select: (state) => state.location.pathname === "/login" });
	return (
		<RouterProvider
			navigate={(to, options) => router.navigate({ ...options, to })}
			useHref={(to) => router.buildLocation({ to }).href}
		>
			{isDevEnvironment ? <DevBanner /> : null}
			{me && !isLogin ? (
				<AppLayout me={me}>
					<Outlet />
				</AppLayout>
			) : (
				<Outlet />
			)}
		</RouterProvider>
	);
}

/** Shown whenever the API runs with dev login: nobody should take this data for real people or companies. */
function DevBanner() {
	const { t } = useTranslation();
	return (
		<div role="note" className="dev-banner">
			<TriangleAlert className="size-3.5 shrink-0" aria-hidden />
			{t("app.devBanner")}
		</div>
	);
}
