import { Link as RouterLink } from "@tanstack/react-router";
import type { ReactNode } from "react";
import { useTranslation } from "react-i18next";

export function AppLayout({ children }: { children: ReactNode }) {
	const { t } = useTranslation();

	return (
		<div className="flex min-h-screen flex-col md:flex-row">
			<aside className="border-b border-border bg-surface p-4 md:w-60 md:border-r md:border-b-0">
				<div className="mb-4 text-lg font-semibold">{t("app.name")}</div>
				<nav className="flex flex-col gap-1">
					<RouterLink
						to="/positions"
						className="rounded-md px-3 py-2 text-sm hover:bg-default"
						activeProps={{ className: "bg-accent-soft text-accent" }}
					>
						{t("app.nav.positions")}
					</RouterLink>
				</nav>
			</aside>
			<main className="flex-1 p-4 md:p-8">{children}</main>
		</div>
	);
}
