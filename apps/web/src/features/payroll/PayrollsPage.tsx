import { EmptyState, Spinner, Table, Tabs, Tooltip } from "@heroui/react";
import { useQuery } from "@tanstack/react-query";
import { getRouteApi, Link as RouterLink } from "@tanstack/react-router";
import { TriangleAlert } from "lucide-react";
import { useRef } from "react";
import { useTranslation } from "react-i18next";
import { AddButton } from "#/components/ui/AddButton";
import { OpenIcon, openLinkClass } from "#/components/ui/OpenLink";
import { CompanySelect } from "#/features/companies/CompanySelect";
import { activeCompaniesQuery } from "#/features/companies/companiesQueries";
import { Notice } from "#/features/time-absence/Notice";
import { currentYear } from "#/features/time-absence/types";
import { YearSelect } from "#/features/time-absence/YearSelect";
import { problemMessage } from "#/forms/feedback";
import { formatDecimal } from "#/lib/money";
import { PayrollSettingsTab } from "./PayrollSettingsTab";
import { PayrollStatusChip } from "./PayrollStatusChip";
import { PayrollWizard, type PayrollWizardHandle } from "./PayrollWizard";
import { payrollsQuery } from "./payrollQueries";
import { formatPeriod, PAYROLL_TABS, type PayrollKind, type PayrollTab } from "./types";

const route = getRouteApi("/payrolls/");

/** "Listy płac" (Kadry, Administrator): lists of a company in a year and the company's payroll settings. */
export function PayrollsPage() {
	const { t } = useTranslation();
	const search = route.useSearch();
	const navigate = route.useNavigate();
	const { data: companies } = useQuery(activeCompaniesQuery());
	// Without a choice in the address the first company is shown, so the page is never empty.
	const companyId = search.companyId ?? companies?.items[0]?.id ?? "";
	const year = search.year ?? currentYear();
	const tab: PayrollTab = search.tab ?? "lists";

	return (
		<section className="flex flex-col gap-4">
			<header>
				<h1 className="text-2xl font-semibold">{t("payroll.title")}</h1>
				<p className="max-w-3xl text-sm text-muted">{t("payroll.hint")}</p>
			</header>
			<div className="flex flex-wrap items-end gap-3">
				<CompanySelect
					className="w-56"
					label={t("payroll.company")}
					value={companyId}
					onChange={(id) =>
						void navigate({
							search: (prev) => ({ ...prev, companyId: id || undefined }),
							replace: true,
						})
					}
				/>
				{tab === "lists" ? (
					<YearSelect
						value={year}
						onChange={(value) =>
							void navigate({ search: (prev) => ({ ...prev, year: value }), replace: true })
						}
					/>
				) : null}
			</div>
			{companyId ? (
				<Tabs
					selectedKey={tab}
					onSelectionChange={(key) =>
						void navigate({
							search: (prev) => ({
								...prev,
								tab: key === "lists" ? undefined : (key as PayrollTab),
							}),
							replace: true,
						})
					}
				>
					<Tabs.ListContainer>
						<Tabs.List aria-label={t("payroll.title")}>
							{PAYROLL_TABS.map((item) => (
								<Tabs.Tab key={item} id={item}>
									{t(`payroll.tabs.${item}`)}
									<Tabs.Indicator />
								</Tabs.Tab>
							))}
						</Tabs.List>
					</Tabs.ListContainer>
					<Tabs.Panel id="lists" className="pt-4">
						<PayrollList companyId={companyId} year={year} />
					</Tabs.Panel>
					<Tabs.Panel id="settings" className="pt-4">
						<PayrollSettingsTab companyId={companyId} />
					</Tabs.Panel>
				</Tabs>
			) : (
				<p className="text-sm text-muted">{t("payroll.chooseCompany")}</p>
			)}
		</section>
	);
}

function PayrollList({ companyId, year }: { companyId: string; year: number }) {
	const { t } = useTranslation();
	const wizard = useRef<PayrollWizardHandle>(null);
	const { data, isPending, error } = useQuery(payrollsQuery(companyId, year));

	return (
		<div className="flex flex-col gap-3">
			<div className="flex gap-2">
				<AddButton
					label={t("payroll.wizard.title")}
					onPress={() => wizard.current?.create(companyId, year)}
				/>
			</div>
			{error ? <Notice status="danger">{problemMessage(t, error)}</Notice> : null}
			{isPending ? (
				<Spinner aria-label={t("common.loading")} />
			) : (
				<Table>
					<Table.ScrollContainer>
						<Table.Content aria-label={t("payroll.title")} className="min-w-[1080px]">
							<Table.Header>
								<Table.Column isRowHeader>{t("payroll.fields.number")}</Table.Column>
								<Table.Column>{t("payroll.fields.kind")}</Table.Column>
								<Table.Column>{t("payroll.fields.period")}</Table.Column>
								<Table.Column>{t("payroll.fields.paymentDate")}</Table.Column>
								<Table.Column>{t("payroll.fields.status")}</Table.Column>
								<Table.Column className="text-right">{t("payroll.fields.entryCount")}</Table.Column>
								<Table.Column className="text-right">{t("payroll.fields.gross")}</Table.Column>
								<Table.Column className="text-right">{t("payroll.fields.net")}</Table.Column>
								<Table.Column className="text-right">{t("payroll.fields.payout")}</Table.Column>
								<Table.Column className="w-12">
									<span className="sr-only">{t("common.open")}</span>
								</Table.Column>
							</Table.Header>
							<Table.Body
								renderEmptyState={() => (
									<EmptyState className="py-8 text-center text-sm text-muted">
										{t("payroll.empty")}
									</EmptyState>
								)}
							>
								{(data ?? []).map((item) => (
									<Table.Row key={item.id} id={item.id}>
										<Table.Cell>
											<RouterLink
												to="/payrolls/$id"
												params={{ id: item.id }}
												className="text-link hover:underline"
											>
												{item.number}
											</RouterLink>
										</Table.Cell>
										<Table.Cell>
											{t(`payroll.kinds.${item.kind as PayrollKind}`)}
											{item.correctsNumber ? (
												<span className="block text-xs text-muted">{item.correctsNumber}</span>
											) : null}
										</Table.Cell>
										<Table.Cell>{formatPeriod(item.periodFrom)}</Table.Cell>
										<Table.Cell>
											<span className="inline-flex items-center gap-1">
												{item.paymentDate}
												{item.paymentDateLate ? (
													<LateIcon label={t("payroll.paymentDateLate")} />
												) : null}
											</span>
										</Table.Cell>
										<Table.Cell>
											<PayrollStatusChip payroll={item} />
										</Table.Cell>
										<Table.Cell className="text-right">{item.entryCount}</Table.Cell>
										<Table.Cell className="text-right">{formatDecimal(item.gross)}</Table.Cell>
										<Table.Cell className="text-right">{formatDecimal(item.net)}</Table.Cell>
										<Table.Cell className="text-right">{formatDecimal(item.payout)}</Table.Cell>
										<Table.Cell>
											<RouterLink
												to="/payrolls/$id"
												params={{ id: item.id }}
												className={openLinkClass}
												aria-label={t("common.open")}
												title={t("common.open")}
											>
												<OpenIcon />
											</RouterLink>
										</Table.Cell>
									</Table.Row>
								))}
							</Table.Body>
						</Table.Content>
					</Table.ScrollContainer>
				</Table>
			)}
			<PayrollWizard ref={wizard} />
		</div>
	);
}

/** Warning icon with its explanation as a tooltip. */
export function LateIcon({ label }: { label: string }) {
	return (
		<Tooltip delay={200}>
			<Tooltip.Trigger aria-label={label}>
				<TriangleAlert className="size-3.5 text-warning" aria-hidden />
			</Tooltip.Trigger>
			<Tooltip.Content>{label}</Tooltip.Content>
		</Tooltip>
	);
}
