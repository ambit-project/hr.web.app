import { Button } from "@heroui/react";
import { useQueryClient } from "@tanstack/react-query";
import { Link as RouterLink, useRouter } from "@tanstack/react-router";
import { LogOut } from "lucide-react";
import type { ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { logoutFn, type Role } from "#/features/auth/auth.functions";
import { hasRole, type Me } from "#/features/auth/session";
import { ThemeToggle } from "#/features/theme/ThemeToggle";

type NavItem = {
	to:
		| "/employees"
		| "/ppk"
		| "/payrolls"
		| "/pit11"
		| "/tax-summary"
		| "/calendar"
		| "/holidays"
		| "/positions"
		| "/pay-components"
		| "/zus-codes"
		| "/tax-offices"
		| "/companies"
		| "/legal-parameters"
		| "/users"
		| "/audit"
		| "/my";
	label:
		| "app.nav.employees"
		| "app.nav.ppk"
		| "app.nav.payrolls"
		| "app.nav.pit11"
		| "app.nav.taxSummary"
		| "app.nav.calendar"
		| "app.nav.holidays"
		| "app.nav.positions"
		| "app.nav.payComponents"
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
	label?:
		| "app.nav.staff"
		| "app.nav.pay"
		| "app.nav.workTime"
		| "app.nav.dictionaries"
		| "app.nav.administration";
	items: NavItem[];
};

const STAFF: Role[] = ["kadry", "administrator"];

// Menu mirrors the database rules only to hide what would end in 403; the database stays the authority.
const NAV: NavSection[] = [
	{ items: [{ to: "/my", label: "app.nav.my", roles: ["pracownik"] }] },
	{
		label: "app.nav.staff",
		items: [
			{ to: "/employees", label: "app.nav.employees", roles: STAFF },
			{ to: "/ppk", label: "app.nav.ppk", roles: STAFF },
		],
	},
	{
		label: "app.nav.pay",
		items: [
			{ to: "/payrolls", label: "app.nav.payrolls", roles: STAFF },
			{ to: "/pit11", label: "app.nav.pit11", roles: STAFF },
			{ to: "/tax-summary", label: "app.nav.taxSummary", roles: STAFF },
		],
	},
	{
		label: "app.nav.workTime",
		items: [
			{ to: "/calendar", label: "app.nav.calendar", roles: STAFF },
			{ to: "/holidays", label: "app.nav.holidays", roles: STAFF },
		],
	},
	{
		label: "app.nav.dictionaries",
		items: [
			{ to: "/positions", label: "app.nav.positions", roles: STAFF },
			{ to: "/pay-components", label: "app.nav.payComponents", roles: STAFF },
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
			<aside className="app-sidebar flex flex-col md:w-[248px] md:shrink-0">
				<div className="app-sidebar__brand">{t("app.name")}</div>
				<nav className="flex flex-col gap-5 py-3">
					{NAV.map((section) => {
						const items = section.items.filter((item) => hasRole(me, ...item.roles));
						if (items.length === 0) return null;
						return (
							<div key={section.label ?? "main"} className="flex flex-col gap-1">
								{section.label ? (
									<div className="app-sidebar__section">{t(section.label)}</div>
								) : null}
								{items.map((item) => (
									<RouterLink
										key={item.to}
										to={item.to}
										className="app-sidebar__link"
										activeProps={{ className: "is-active" }}
									>
										{t(item.label)}
									</RouterLink>
								))}
							</div>
						);
					})}
				</nav>
			</aside>
			<div className="flex min-w-0 flex-1 flex-col">
				<header className="app-topbar flex h-14 items-center justify-end gap-3 px-4 md:px-8">
					<div className="min-w-0 text-right">
						<div className="truncate text-sm font-medium">{me.displayName}</div>
						<div className="app-topbar__muted truncate text-xs">
							{me.roles.map((role) => t(`roles.${role as Role}`)).join(", ")}
						</div>
					</div>
					<ThemeToggle />
					<Button size="sm" variant="ghost" onPress={() => void signOut()}>
						<LogOut className="size-4" />
						{t("auth.logout")}
					</Button>
				</header>
				<main className="flex-1 p-4 md:p-8">{children}</main>
			</div>
		</div>
	);
}
