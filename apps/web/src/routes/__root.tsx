import { Toast } from "@heroui/react";
import type { QueryClient } from "@tanstack/react-query";
import {
	createRootRouteWithContext,
	HeadContent,
	type NavigateOptions,
	Outlet,
	Scripts,
	type ToOptions,
	useRouter,
} from "@tanstack/react-router";
import type { ReactNode } from "react";
import { RouterProvider } from "react-aria-components";
import { AppLayout } from "#/components/layout/AppLayout";
import { DEFAULT_LANGUAGE, i18next } from "#/i18n";
import appCss from "#/styles/app.css?url";

// HeroUI links with href navigate through TanStack Router instead of reloading the page.
declare module "react-aria-components" {
	interface RouterConfig {
		href: ToOptions["to"];
		routerOptions: Omit<NavigateOptions, keyof ToOptions>;
	}
}

export const Route = createRootRouteWithContext<{ queryClient: QueryClient }>()({
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
	return (
		<RouterProvider
			navigate={(to, options) => router.navigate({ ...options, to })}
			useHref={(to) => router.buildLocation({ to }).href}
		>
			<AppLayout>
				<Outlet />
			</AppLayout>
		</RouterProvider>
	);
}
