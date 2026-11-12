import { Button } from "@heroui/react";
import { useQueryClient } from "@tanstack/react-query";
import { Link as RouterLink, useRouter } from "@tanstack/react-router";
import type { ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { logoutFn, type Role } from "#/features/auth/auth.functions";
import { hasRole, type Me } from "#/features/auth/session";

type NavItem = {
	to: "/positions" | "/companies" | "/my";
	label: "app.nav.positions" | "app.nav.companies" | "app.nav.my";
	roles: Role[];
};

// Menu mirrors the database rules only to hide what would end in 403; the database stays the authority.
const NAV: NavItem[] = [
	{ to: "/positions", label: "app.nav.positions", roles: ["kadry", "administrator"] },
	{ to: "/companies", label: "app.nav.companies", roles: ["kadry", "administrator"] },
	{ to: "/my", label: "app.nav.my", roles: ["pracownik"] },
];

export function AppLayout({ me, children }: { me: Me; children: ReactNode }) {
	const { t } = useTranslation();
	const router = useRouter();
	const queryClient = useQueryClient();

	const signOut = async () => {
		await logoutFn();
		queryClient.clear();
		// Straight to the login page: the next person should not land on this account's last page.
		await router.navigate({ to: "/login" });
		await router.invalidate();
	};

	return (
		<div className="flex min-h-screen flex-col md:flex-row">
			<aside className="flex flex-col border-b border-border bg-surface p-4 md:w-60 md:border-r md:border-b-0">
				<div className="mb-4 text-lg font-semibold">{t("app.name")}</div>
				<nav className="flex flex-col gap-1">
					{NAV.filter((item) => hasRole(me, ...item.roles)).map((item) => (
						<RouterLink
							key={item.to}
							to={item.to}
							className="rounded-md px-3 py-2 text-sm hover:bg-default"
							activeProps={{ className: "bg-accent-soft text-accent" }}
						>
							{t(item.label)}
						</RouterLink>
					))}
				</nav>
				<div className="mt-4 flex items-center justify-between gap-2 border-t border-border pt-4 md:mt-auto">
					<div className="min-w-0">
						<div className="truncate text-sm font-medium">{me.displayName}</div>
						<div className="truncate text-xs text-muted">
							{me.roles.map((role) => t(`roles.${role as Role}`)).join(", ")}
						</div>
					</div>
					<Button size="sm" variant="ghost" onPress={() => void signOut()}>
						{t("auth.logout")}
					</Button>
				</div>
			</aside>
			<main className="flex-1 p-4 md:p-8">{children}</main>
		</div>
	);
}
