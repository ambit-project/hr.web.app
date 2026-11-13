import { Button } from "@heroui/react";
import { useQueryClient } from "@tanstack/react-query";
import { Link as RouterLink, useRouter } from "@tanstack/react-router";
import type { ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { logoutFn, type Role } from "#/features/auth/auth.functions";
import { hasRole, type Me } from "#/features/auth/session";
import { ThemeSwitch } from "#/features/theme/ThemeSwitch";

type NavItem = {
	to:
		| "/employees"
		| "/positions"
		| "/zus-codes"
		| "/tax-offices"
		| "/companies"
		| "/legal-parameters"
		| "/users"
		| "/audit"
		| "/my";
	label:
		| "app.nav.employees"
		| "app.nav.positions"
		| "app.nav.zusCodes"
		| "app.nav.taxOffices"
		| "app.nav.companies"
		| "app.nav.legalParameters"
		| "app.nav.users"
		| "app.nav.audit"
		| "app.nav.my";
	roles: Role[];
};

type NavSection = {
	label?: "app.nav.staff" | "app.nav.dictionaries" | "app.nav.administration";
	items: NavItem[];
};

const STAFF: Role[] = ["kadry", "administrator"];

// Menu mirrors the database rules only to hide what would end in 403; the database stays the authority.
const NAV: NavSection[] = [
	{ items: [{ to: "/my", label: "app.nav.my", roles: ["pracownik"] }] },
	{
		label: "app.nav.staff",
		items: [{ to: "/employees", label: "app.nav.employees", roles: STAFF }],
	},
	{
		label: "app.nav.dictionaries",
		items: [
			{ to: "/positions", label: "app.nav.positions", roles: STAFF },
			{ to: "/zus-codes", label: "app.nav.zusCodes", roles: STAFF },
			{ to: "/tax-offices", label: "app.nav.taxOffices", roles: STAFF },
		],
	},
	{
		label: "app.nav.administration",
		items: [
			{ to: "/companies", label: "app.nav.companies", roles: STAFF },
			{ to: "/legal-parameters", label: "app.nav.legalParameters", roles: ["administrator"] },
			{ to: "/users", label: "app.nav.users", roles: ["administrator"] },
			{ to: "/audit", label: "app.nav.audit", roles: ["administrator"] },
		],
	},
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
				<nav className="flex flex-col gap-4">
					{NAV.map((section) => {
						const items = section.items.filter((item) => hasRole(me, ...item.roles));
						if (items.length === 0) return null;
						return (
							<div key={section.label ?? "main"} className="flex flex-col gap-1">
								{section.label ? (
									<div className="px-3 text-xs font-medium uppercase text-muted">
										{t(section.label)}
									</div>
								) : null}
								{items.map((item) => (
									<RouterLink
										key={item.to}
										to={item.to}
										className="rounded-md px-3 py-2 text-sm hover:bg-default"
										activeProps={{ className: "bg-accent-soft text-accent" }}
									>
										{t(item.label)}
									</RouterLink>
								))}
							</div>
						);
					})}
				</nav>
				<div className="mt-4 md:mt-auto">
					<ThemeSwitch />
				</div>
				<div className="mt-4 flex items-center justify-between gap-2 border-t border-border pt-4">
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
