import { Tabs } from "@heroui/react";
import { useQuery } from "@tanstack/react-query";
import { getRouteApi } from "@tanstack/react-router";
import { useTranslation } from "react-i18next";
import { CompanySelect } from "#/features/companies/CompanySelect";
import { activeCompaniesQuery } from "#/features/companies/companiesQueries";
import { currentYear } from "#/features/time-absence/types";
import { CandidatesTab } from "./CandidatesTab";
import { ReenrollmentTab } from "./ReenrollmentTab";
import { SettingsTab } from "./SettingsTab";
import { nextCycleYear, PPK_TABS, type PpkTab, todayIso } from "./types";

const route = getRouteApi("/ppk/");

/** "PPK" (Kadry, Administrator): the company's PPK settings, people to enrol and the 4-year re-enrolment. */
export function PpkPage() {
	const { t } = useTranslation();
	const search = route.useSearch();
	const navigate = route.useNavigate();
	const { data: companies } = useQuery(activeCompaniesQuery());
	// Without a choice in the address the first company is shown, so the page is never empty.
	const companyId = search.companyId ?? companies?.items[0]?.id ?? "";
	const tab: PpkTab = search.tab ?? "settings";
	const on = search.on ?? todayIso();
	const year = search.year ?? nextCycleYear(currentYear());

	return (
		<section className="flex flex-col gap-4">
			<header>
				<h1 className="text-2xl font-semibold">{t("ppk.title")}</h1>
				<p className="max-w-3xl text-sm text-muted">{t("ppk.hint")}</p>
			</header>
			<CompanySelect
				className="w-56"
				label={t("ppk.company")}
				value={companyId}
				onChange={(id) =>
					void navigate({
						search: (prev) => ({ ...prev, companyId: id || undefined }),
						replace: true,
					})
				}
			/>
			{companyId ? (
				<Tabs
					selectedKey={tab}
					onSelectionChange={(key) =>
						void navigate({
							search: (prev) => ({
								...prev,
								tab: key === "settings" ? undefined : (key as PpkTab),
							}),
							replace: true,
						})
					}
				>
					<Tabs.ListContainer>
						<Tabs.List aria-label={t("ppk.title")}>
							{PPK_TABS.map((item) => (
								<Tabs.Tab key={item} id={item}>
									{t(`ppk.tabs.${item}`)}
									<Tabs.Indicator />
								</Tabs.Tab>
							))}
						</Tabs.List>
					</Tabs.ListContainer>
					<Tabs.Panel id="settings" className="pt-4">
						<SettingsTab companyId={companyId} />
					</Tabs.Panel>
					<Tabs.Panel id="candidates" className="pt-4">
						<CandidatesTab
							companyId={companyId}
							on={on}
							onDateChange={(value) =>
								void navigate({ search: (prev) => ({ ...prev, on: value }), replace: true })
							}
						/>
					</Tabs.Panel>
					<Tabs.Panel id="reenrollment" className="pt-4">
						<ReenrollmentTab
							companyId={companyId}
							year={year}
							onYearChange={(value) =>
								void navigate({ search: (prev) => ({ ...prev, year: value }), replace: true })
							}
						/>
					</Tabs.Panel>
				</Tabs>
			) : (
				<p className="text-sm text-muted">{t("ppk.chooseCompany")}</p>
			)}
		</section>
	);
}
