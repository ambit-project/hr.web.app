import { Spinner } from "@heroui/react";
import { useQuery } from "@tanstack/react-query";
import { getRouteApi } from "@tanstack/react-router";
import { useTranslation } from "react-i18next";
import type { GetTaxSummaryItem } from "#/api/models";
import { CompanySelect } from "#/features/companies/CompanySelect";
import { activeCompaniesQuery } from "#/features/companies/companiesQueries";
import { Notice } from "#/features/time-absence/Notice";
import { currentYear } from "#/features/time-absence/types";
import { YearSelect } from "#/features/time-absence/YearSelect";
import { problemMessage } from "#/forms/feedback";
import { formatDecimal } from "#/lib/money";
import { taxSummaryQuery } from "./documentsQueries";

const route = getRouteApi("/tax-summary/");

const MONTH = new Intl.DateTimeFormat("pl-PL", { month: "long", timeZone: "UTC" });

const AMOUNTS = [
	"employmentAdvances",
	"mandateAdvances",
	"specificWorkAdvances",
	"advancesTotal",
	"mandateFlatTax",
	"specificWorkFlatTax",
	"flatTaxTotal",
] as const;

/** "Płace → Zestawienie PIT-4R/8AR" (R12; Kadry, Administrator): advances and flat tax by payment month. */
export function TaxSummaryPage() {
	const { t } = useTranslation();
	const search = route.useSearch();
	const navigate = route.useNavigate();
	const { data: companies } = useQuery(activeCompaniesQuery());
	const companyId = search.companyId ?? companies?.items[0]?.id ?? "";
	const year = search.year ?? currentYear();

	return (
		<section className="flex flex-col gap-4">
			<header>
				<h1 className="text-2xl font-semibold">{t("documents.taxSummary.title")}</h1>
				<p className="max-w-3xl text-sm text-muted">{t("documents.taxSummary.hint")}</p>
			</header>
			<div className="flex flex-wrap items-end gap-3">
				<CompanySelect
					className="w-56"
					label={t("documents.company")}
					value={companyId}
					onChange={(id) =>
						void navigate({
							search: (prev) => ({ ...prev, companyId: id || undefined }),
							replace: true,
						})
					}
				/>
				<YearSelect
					value={year}
					onChange={(value) =>
						void navigate({ search: (prev) => ({ ...prev, year: value }), replace: true })
					}
				/>
			</div>
			{companyId ? (
				<SummaryTable companyId={companyId} year={year} />
			) : (
				<p className="text-sm text-muted">{t("documents.chooseCompany")}</p>
			)}
		</section>
	);
}

function SummaryTable({ companyId, year }: { companyId: string; year: number }) {
	const { t } = useTranslation();
	const { data, isPending, error } = useQuery(taxSummaryQuery(companyId, year));
	if (error) return <Notice status="danger">{problemMessage(t, error)}</Notice>;
	if (isPending) return <Spinner aria-label={t("common.loading")} />;

	const rows = data ?? [];
	const total = (key: (typeof AMOUNTS)[number]) =>
		Math.round(rows.reduce((sum, row) => sum + row[key], 0) * 100) / 100;

	// A plain table: the two-level header (advances / flat tax) needs colSpan, which the HeroUI table lacks.
	return (
		<div className="overflow-x-auto">
			<table className="table__content w-full min-w-[960px] border-collapse">
				<thead>
					<tr>
						<th rowSpan={2} className="table__column text-left">
							{t("documents.taxSummary.month")}
						</th>
						<th colSpan={5} className="table__column text-center">
							{t("documents.taxSummary.advances")}
						</th>
						<th colSpan={4} className="table__column text-center">
							{t("documents.taxSummary.flat")}
						</th>
					</tr>
					<tr>
						<th className="table__column text-right">{t("documents.taxSummary.employment")}</th>
						<th className="table__column text-right">{t("documents.taxSummary.mandate")}</th>
						<th className="table__column text-right">{t("documents.taxSummary.specificWork")}</th>
						<th className="table__column text-right">{t("documents.taxSummary.total")}</th>
						<th className="table__column text-right">{t("documents.taxSummary.people")}</th>
						<th className="table__column text-right">{t("documents.taxSummary.mandate")}</th>
						<th className="table__column text-right">{t("documents.taxSummary.specificWork")}</th>
						<th className="table__column text-right">{t("documents.taxSummary.total")}</th>
						<th className="table__column text-right">{t("documents.taxSummary.people")}</th>
					</tr>
				</thead>
				<tbody className="table__body">
					{rows.map((row: GetTaxSummaryItem) => (
						<tr key={row.month} className="table__row">
							<td className="table__cell capitalize">
								{MONTH.format(new Date(Date.UTC(year, row.month - 1, 1)))}
							</td>
							<td className="table__cell text-right">{formatDecimal(row.employmentAdvances)}</td>
							<td className="table__cell text-right">{formatDecimal(row.mandateAdvances)}</td>
							<td className="table__cell text-right">{formatDecimal(row.specificWorkAdvances)}</td>
							<td className="table__cell text-right font-semibold">
								{formatDecimal(row.advancesTotal)}
							</td>
							<td className="table__cell text-right">{row.advancePeople}</td>
							<td className="table__cell text-right">{formatDecimal(row.mandateFlatTax)}</td>
							<td className="table__cell text-right">{formatDecimal(row.specificWorkFlatTax)}</td>
							<td className="table__cell text-right font-semibold">
								{formatDecimal(row.flatTaxTotal)}
							</td>
							<td className="table__cell text-right">{row.flatTaxPeople}</td>
						</tr>
					))}
					<tr className="table__row font-semibold">
						<td className="table__cell">{t("documents.total")}</td>
						<td className="table__cell text-right">{formatDecimal(total("employmentAdvances"))}</td>
						<td className="table__cell text-right">{formatDecimal(total("mandateAdvances"))}</td>
						<td className="table__cell text-right">
							{formatDecimal(total("specificWorkAdvances"))}
						</td>
						<td className="table__cell text-right">{formatDecimal(total("advancesTotal"))}</td>
						<td className="table__cell" />
						<td className="table__cell text-right">{formatDecimal(total("mandateFlatTax"))}</td>
						<td className="table__cell text-right">
							{formatDecimal(total("specificWorkFlatTax"))}
						</td>
						<td className="table__cell text-right">{formatDecimal(total("flatTaxTotal"))}</td>
						<td className="table__cell" />
					</tr>
				</tbody>
			</table>
		</div>
	);
}
