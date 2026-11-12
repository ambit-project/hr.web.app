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
import type { ReactNode } from "react";
import { RouterProvider } from "react-aria-components";
import { AppLayout } from "#/components/layout/AppLayout";
import { getMeFn } from "#/features/auth/auth.functions";
import type { Me } from "#/features/auth/session";
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
	beforeLoad: async ({ location }) => {
		const me = await getMeFn();
		if (!me && location.pathname !== "/login") {
			throw redirect({ to: "/login", search: { redirect: location.href } });
		}
		return { me };
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
		<html lang={DEFAULT_LANGUAGE}>
			<head>
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
	const { me } = Route.useRouteContext();
	const isLogin = useRouterState({ select: (state) => state.location.pathname === "/login" });
	return (
		<RouterProvider
			navigate={(to, options) => router.navigate({ ...options, to })}
			useHref={(to) => router.buildLocation({ to }).href}
		>
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
