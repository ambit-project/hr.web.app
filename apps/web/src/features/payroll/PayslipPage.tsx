import { Button, Spinner } from "@heroui/react";
import { useQuery } from "@tanstack/react-query";
import { getRouteApi, Link as RouterLink } from "@tanstack/react-router";
import type { TFunction } from "i18next";
import { ChevronDown, ChevronRight } from "lucide-react";
import { Fragment, useState } from "react";
import { useTranslation } from "react-i18next";
import type { GetPayrollEntryLine } from "#/api/models";

type PayslipLine = Omit<GetPayrollEntryLine, "trace"> & { trace: unknown };

import { Notice } from "#/features/time-absence/Notice";
import { problemMessage } from "#/forms/feedback";
import { formatDecimal } from "#/lib/money";
import { PayrollStatusChip } from "./PayrollStatusChip";
import { payrollEntryQuery, payrollQuery, yearTotalsQuery } from "./payrollQueries";
import { TraceView } from "./TraceView";
import {
	formatLineRate,
	formatPeriod,
	formatTraceNumber,
	groupLines,
	isLineCode,
	type LineSide,
} from "./types";
import { WarningNotices } from "./Warnings";

const route = getRouteApi("/payroll-entries/$id");

/** One payslip: person and contract, lines by group with their trace, warnings and the year totals. */
export function PayslipPage() {
	const { t } = useTranslation();
	const { id } = route.useParams();
	const { data: entry, isPending, error } = useQuery(payrollEntryQuery(id));
	const { data: payroll } = useQuery({
		...payrollQuery(entry?.payrollId ?? ""),
		enabled: Boolean(entry),
	});

	if (error) return <Notice status="danger">{problemMessage(t, error)}</Notice>;
	if (isPending || !entry) return <Spinner aria-label={t("common.loading")} />;

	const name = `${entry.firstName} ${entry.lastName}`;
	const year = payroll ? Number(payroll.periodFrom.slice(0, 4)) : null;
	const hours: [string, number | null][] = [
		[t("payroll.payslip.normHours"), entry.normHours],
		[t("payroll.payslip.workedHours"), entry.workedHours],
		[t("payroll.payslip.overtimeHours"), entry.overtimeHours],
	];

	return (
		<section className="flex flex-col gap-4">
			<RouterLink
				to="/payrolls/$id"
				params={{ id: entry.payrollId }}
				className="text-sm text-muted hover:underline"
			>
				← {t("payroll.page.title", { number: entry.payrollNumber })}
			</RouterLink>
			<header className="flex flex-wrap items-center gap-3">
				<h1 className="text-2xl font-semibold">{t("payroll.payslip.title", { name })}</h1>
				{payroll ? <PayrollStatusChip payroll={payroll} /> : null}
			</header>
			<dl className="grid max-w-2xl grid-cols-[12rem_1fr] gap-x-4 gap-y-2 text-sm">
				<dt className="text-muted">{t("payroll.payslip.employee")}</dt>
				<dd>
					<RouterLink
						to="/employees/$id"
						params={{ id: entry.employeeId }}
						className="text-link hover:underline"
					>
						{name}
					</RouterLink>
					<span className="ml-2 text-xs text-muted">{entry.employeeNumber}</span>
				</dd>
				<dt className="text-muted">{t("payroll.payslip.contract")}</dt>
				<dd>
					<RouterLink
						to="/employments/$id"
						params={{ id: entry.employmentId }}
						className="text-link hover:underline"
					>
						{entry.employmentNumber}
					</RouterLink>
				</dd>
				<dt className="text-muted">{t("payroll.payslip.payroll")}</dt>
				<dd>
					{entry.payrollNumber}
					{payroll ? (
						<span className="ml-2 text-xs text-muted">
							{formatPeriod(payroll.periodFrom)} · {t("payroll.fields.paymentDate")}{" "}
							{payroll.paymentDate}
						</span>
					) : null}
				</dd>
				{hours
					.filter(([, value]) => value !== null)
					.map(([label, value]) => (
						<Fragment key={label}>
							<dt className="text-muted">{label}</dt>
							<dd className="tabular-nums">{formatTraceNumber(value ?? 0)}</dd>
						</Fragment>
					))}
			</dl>
			<dl className="flex flex-wrap gap-x-8 gap-y-2 text-sm">
				{(
					[
						["gross", entry.gross],
						["net", entry.net],
						["payout", entry.payout],
					] as const
				).map(([key, value]) => (
					<div key={key} className="flex flex-col">
						<dt className="text-xs uppercase text-muted">{t(`payroll.fields.${key}`)}</dt>
						<dd className="text-lg font-semibold tabular-nums">{formatDecimal(value)} zł</dd>
					</div>
				))}
			</dl>
			{entry.warnings.length > 0 ? <WarningNotices warnings={entry.warnings} /> : null}
			<LinesTable lines={entry.lines} />
			{year !== null ? <YearTotals employeeId={entry.employeeId} year={year} /> : null}
		</section>
	);
}

function lineName(t: TFunction<"translation">, line: PayslipLine): string {
	if (isLineCode(line.code)) return t(`payroll.codes.${line.code}`);
	return line.componentName ?? line.code;
}

const FLAGS = ["inZusBase", "inHealthBase", "taxable", "inSicknessBase"] as const;

/** Which bases an income line is in, e.g. "Przychód: ZUS, zdrowotna, PIT". */
function flagsText(t: TFunction<"translation">, line: PayslipLine): string | null {
	if (line.side !== "income") return null;
	const flags = FLAGS.filter((flag) => line[flag]).map((flag) =>
		t(`payroll.payslip.flagNames.${flag}`),
	);
	return flags.length > 0
		? t("payroll.payslip.flags", { flags: flags.join(", ") })
		: t("payroll.payslip.notInBase");
}

/**
 * Lines grouped like a payslip. A plain table with the global ERP table classes, because a trace row spans all
 * columns (colSpan), which the HeroUI table does not offer.
 */
function LinesTable({ lines }: { lines: PayslipLine[] }) {
	const { t } = useTranslation();
	const [open, setOpen] = useState<ReadonlySet<number>>(new Set());
	const toggle = (seq: number) =>
		setOpen((prev) => {
			const next = new Set(prev);
			if (next.has(seq)) next.delete(seq);
			else next.add(seq);
			return next;
		});

	return (
		<div className="overflow-x-auto">
			<table className="table__content w-full min-w-[860px] border-collapse">
				<thead>
					<tr>
						<th className="table__column w-10">
							<span className="sr-only">{t("payroll.payslip.trace")}</span>
						</th>
						<th className="table__column text-left">{t("payroll.payslip.code")}</th>
						<th className="table__column text-left">{t("payroll.payslip.name")}</th>
						<th className="table__column text-right">{t("payroll.payslip.base")}</th>
						<th className="table__column text-right">{t("payroll.payslip.rate")}</th>
						<th className="table__column text-right">{t("payroll.payslip.amount")}</th>
					</tr>
				</thead>
				{groupLines(lines).map((group) => (
					<tbody key={group.group} className="table__body">
						<tr className="table__row">
							<th
								colSpan={6}
								scope="colgroup"
								className="table__cell text-left text-sm font-semibold"
							>
								{t(`payroll.payslip.groups.${group.group}`)}
							</th>
						</tr>
						{group.lines.map((line) => {
							const expanded = open.has(line.seq);
							const flags = flagsText(t, line);
							return (
								<Fragment key={line.seq}>
									<tr className="table__row">
										<td className="table__cell">
											<Button
												isIconOnly
												size="sm"
												variant="ghost"
												aria-expanded={expanded}
												aria-label={
													expanded ? t("payroll.payslip.hideTrace") : t("payroll.payslip.showTrace")
												}
												onPress={() => toggle(line.seq)}
											>
												{expanded ? (
													<ChevronDown className="size-4" />
												) : (
													<ChevronRight className="size-4" />
												)}
											</Button>
										</td>
										<td className="table__cell">
											<code>{line.code}</code>
										</td>
										<td className="table__cell">
											{lineName(t, line)}
											{group.group === "ppk" || line.side === "info" ? (
												<span className="ml-2 text-xs text-muted">
													{t(`payroll.sides.${line.side as LineSide}`)}
												</span>
											) : null}
											{flags ? <span className="block text-xs text-muted">{flags}</span> : null}
										</td>
										<td className="table__cell text-right">
											{line.base === null ? "" : formatDecimal(line.base)}
										</td>
										<td className="table__cell text-right">{formatLineRate(line.rate)}</td>
										<td className="table__cell text-right">{formatDecimal(line.amount)}</td>
									</tr>
									{expanded ? (
										<tr className="table__row">
											<td className="table__cell" />
											<td colSpan={5} className="table__cell py-2">
												<TraceView trace={line.trace} />
											</td>
										</tr>
									) : null}
								</Fragment>
							);
						})}
						{group.total !== null ? (
							<tr className="table__row">
								<td colSpan={5} className="table__cell text-right font-medium">
									{t("payroll.payslip.total")}
								</td>
								<td className="table__cell text-right font-semibold">
									{formatDecimal(group.total)}
								</td>
							</tr>
						) : null}
					</tbody>
				))}
			</table>
		</div>
	);
}

const TOTALS = [
	"pensionBase",
	"taxBaseEmployment",
	"taxBaseAll",
	"exemptIncome",
	"authorCosts",
	"employeeCosts",
] as const;

function YearTotals({ employeeId, year }: { employeeId: string; year: number }) {
	const { t } = useTranslation();
	const { data } = useQuery(yearTotalsQuery(employeeId, year));
	if (!data) return null;
	return (
		<div className="flex flex-col gap-2 pt-2">
			<h2 className="text-lg font-semibold">{t("payroll.payslip.yearTotals.title", { year })}</h2>
			<dl className="grid max-w-xl grid-cols-[1fr_auto] gap-x-6 gap-y-1 text-sm">
				{TOTALS.map((key) => (
					<Fragment key={key}>
						<dt className="text-muted">{t(`payroll.payslip.yearTotals.${key}`)}</dt>
						<dd className="text-right tabular-nums">{formatDecimal(data[key])}</dd>
					</Fragment>
				))}
			</dl>
		</div>
	);
}
