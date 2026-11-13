import { useQuery } from "@tanstack/react-query";
import { getRouteApi, Link as RouterLink } from "@tanstack/react-router";
import { useTranslation } from "react-i18next";
import { employeeQuery } from "#/features/employees/employeesQueries";
import { formatPeriod } from "#/features/payroll/types";
import { currentYear } from "#/features/time-absence/types";
import { YearSelect } from "#/features/time-absence/YearSelect";
import { formatDecimal } from "#/lib/money";
import { wageCardQuery } from "./documentsQueries";
import { DocField, PrintPage } from "./PrintLayout";
import { sumWageCard, WAGE_CARD_AMOUNTS } from "./types";

const route = getRouteApi("/wage-cards/$employeeId");

/** Wage card of an employee record for a year (R4; Kadry, Administrator) as a printable page. */
export function WageCardPrintPage() {
	const { t } = useTranslation();
	const { employeeId } = route.useParams();
	const search = route.useSearch();
	const navigate = route.useNavigate();
	const year = search.year ?? currentYear();
	const { data: employee } = useQuery(employeeQuery(employeeId));
	const { data: rows, isPending, error } = useQuery(wageCardQuery(employeeId, year));
	const totals = sumWageCard(rows ?? []);

	return (
		<PrintPage
			isPending={isPending}
			error={error}
			back={
				<div className="flex items-end gap-4">
					<RouterLink
						to="/employees/$id"
						params={{ id: employeeId }}
						search={{ tab: "documents" }}
						className="hover:underline"
					>
						{t("documents.back")}
					</RouterLink>
					<YearSelect
						value={year}
						onChange={(value) =>
							void navigate({ search: (prev) => ({ ...prev, year: value }), replace: true })
						}
					/>
				</div>
			}
		>
			<div className="flex flex-col gap-3">
				<h1>{t("documents.wageCard.title", { year })}</h1>
				{employee ? (
					<div className="grid grid-cols-2 gap-1">
						<DocField label={t("documents.payslip.employer")}>{employee.companyShortName}</DocField>
						<DocField label={t("documents.payslip.employee")}>
							{employee.lastName} {employee.firstName}
							<span className="ml-2 font-normal">{employee.number}</span>
						</DocField>
					</div>
				) : null}
				<p className="text-[10px] text-muted">{t("documents.wageCard.hint")}</p>
				<table className="text-[10px]">
					<thead>
						<tr>
							<th>{t("documents.wageCard.columns.paymentDate")}</th>
							<th>{t("documents.wageCard.columns.payroll")}</th>
							<th>{t("documents.wageCard.columns.period")}</th>
							<th>{t("documents.wageCard.columns.contract")}</th>
							{WAGE_CARD_AMOUNTS.map((key) => (
								<th key={key} className="text-right">
									{t(`documents.wageCard.columns.${key}`)}
								</th>
							))}
						</tr>
					</thead>
					<tbody>
						{(rows ?? []).length === 0 ? (
							<tr>
								<td colSpan={4 + WAGE_CARD_AMOUNTS.length}>{t("documents.wageCard.empty")}</td>
							</tr>
						) : null}
						{(rows ?? []).map((row) => (
							<tr key={row.entryId}>
								<td>{row.paymentDate}</td>
								<td>{row.payrollNumber}</td>
								<td>{formatPeriod(row.periodFrom)}</td>
								<td>{row.employmentNumber}</td>
								{WAGE_CARD_AMOUNTS.map((key) => (
									<td key={key} className="text-right">
										{formatDecimal(row[key])}
									</td>
								))}
							</tr>
						))}
						<tr className="font-semibold">
							<td colSpan={4}>{t("documents.total")}</td>
							{WAGE_CARD_AMOUNTS.map((key) => (
								<td key={key} className="text-right">
									{formatDecimal(totals[key])}
								</td>
							))}
						</tr>
					</tbody>
				</table>
			</div>
		</PrintPage>
	);
}
