import { useQuery } from "@tanstack/react-query";
import { getRouteApi, useRouter } from "@tanstack/react-router";
import type { TFunction } from "i18next";
import { useTranslation } from "react-i18next";
import type { GetPayslipLine } from "#/api/models";
import {
	formatLineRate,
	formatPeriod,
	groupLines,
	isLineCode,
	type PayrollKind,
} from "#/features/payroll/types";
import { formatDecimal } from "#/lib/money";
import { payslipQuery } from "./documentsQueries";
import { DocField, PrintPage } from "./PrintLayout";

const route = getRouteApi("/payslips/$entryId");

type ContractKind = "employment" | "mandate" | "specific_work";

function lineName(t: TFunction<"translation">, line: GetPayslipLine): string {
	if (isLineCode(line.code)) return t(`payroll.codes.${line.code}`);
	return line.name ?? line.code;
}

/** Payslip of a closed list (R1–R3) as a printable page; the employee reads their own, staff any. */
export function PayslipPrintPage() {
	const { t } = useTranslation();
	const router = useRouter();
	const { entryId } = route.useParams();
	const { data, isPending, error } = useQuery(payslipQuery(entryId));

	return (
		<PrintPage
			isPending={isPending}
			error={error}
			back={
				<button type="button" className="hover:underline" onClick={() => router.history.back()}>
					{t("documents.back")}
				</button>
			}
		>
			{data ? (
				<div className="flex flex-col gap-3">
					<header className="flex flex-wrap items-baseline justify-between gap-2">
						<h1>{t("documents.payslip.title")}</h1>
						<span>
							{t("documents.payslip.period")} {formatPeriod(data.periodFrom)} ·{" "}
							{t("documents.payslip.paymentDate")} {data.paymentDate}
						</span>
					</header>
					<div className="grid grid-cols-2 gap-1">
						<DocField label={t("documents.payslip.employer")}>
							{data.companyName}
							<div className="font-normal">
								NIP {data.companyNip} · {data.companyAddress}
							</div>
						</DocField>
						<DocField label={t("documents.payslip.employee")}>
							{data.lastName} {data.firstName}
							<div className="font-normal">{data.employeeNumber}</div>
						</DocField>
						<DocField label={t("documents.payslip.contract")}>
							{data.employmentNumber}
							<div className="font-normal">
								{t(`documents.contractKinds.${data.contractKind as ContractKind}`)}
							</div>
						</DocField>
						<DocField label={t("documents.payslip.payroll")}>
							{data.payrollNumber}
							<div className="font-normal">
								{t(`payroll.kinds.${data.payrollKind as PayrollKind}`)}
							</div>
						</DocField>
					</div>
					<table>
						<thead>
							<tr>
								<th>{t("documents.payslip.code")}</th>
								<th>{t("documents.payslip.name")}</th>
								<th className="text-right">{t("documents.payslip.base")}</th>
								<th className="text-right">{t("documents.payslip.rate")}</th>
								<th className="text-right">{t("documents.amount")}</th>
							</tr>
						</thead>
						{groupLines(data.lines).map((group) => (
							<tbody key={group.group}>
								<tr>
									<th colSpan={5}>{t(`payroll.payslip.groups.${group.group}`)}</th>
								</tr>
								{group.lines.map((line) => (
									<tr key={line.seq}>
										<td>
											<code>{line.code}</code>
										</td>
										<td>{lineName(t, line)}</td>
										<td className="text-right">
											{line.base === null || line.base === undefined
												? ""
												: formatDecimal(line.base)}
										</td>
										<td className="text-right">{formatLineRate(line.rate)}</td>
										<td className="text-right">{formatDecimal(line.amount)}</td>
									</tr>
								))}
								{group.total !== null ? (
									<tr>
										<td colSpan={4} className="text-right font-semibold">
											{t("documents.total")}
										</td>
										<td className="text-right font-semibold">{formatDecimal(group.total)}</td>
									</tr>
								) : null}
							</tbody>
						))}
					</table>
					<div className="grid grid-cols-2 gap-3">
						<table>
							<tbody>
								<tr>
									<th colSpan={2}>{t("documents.payslip.bases")}</th>
								</tr>
								{(
									[
										["zusBase", data.zusBase],
										["healthBase", data.healthBase],
										["costs", data.costs],
										["taxBase", data.taxBase],
									] as const
								).map(([key, value]) => (
									<tr key={key}>
										<td>{t(`documents.payslip.${key}`)}</td>
										<td className="text-right">{formatDecimal(value)}</td>
									</tr>
								))}
							</tbody>
						</table>
						<table>
							<tbody>
								{(
									[
										["gross", data.gross],
										["net", data.net],
										["payout", data.payout],
									] as const
								).map(([key, value]) => (
									<tr key={key}>
										<td className="font-semibold">{t(`documents.payslip.${key}`)}</td>
										<td className="text-right text-base font-bold">{formatDecimal(value)} zł</td>
									</tr>
								))}
							</tbody>
						</table>
					</div>
					<p className="text-[10px] text-muted">{t("documents.payslip.footer")}</p>
				</div>
			) : null}
		</PrintPage>
	);
}
