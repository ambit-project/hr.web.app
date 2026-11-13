import { Button, Card, Chip, Spinner, toast } from "@heroui/react";
import { useQuery } from "@tanstack/react-query";
import { getRouteApi } from "@tanstack/react-router";
import { useRef } from "react";
import { useTranslation } from "react-i18next";
import type { GetCalendarMonthDay, GetCalendarYearMonth } from "#/api/models";
import { CompanySelect } from "#/features/companies/CompanySelect";
import { activeCompaniesQuery } from "#/features/companies/companiesQueries";
import { onActionError } from "#/forms/feedback";
import { CalendarDayDialog, type CalendarDayDialogHandle } from "./CalendarDayDialog";
import { Notice } from "./Notice";
import {
	calendarMonthQuery,
	calendarYearQuery,
	holidaysQuery,
	useGenerateCalendar,
} from "./timeAbsenceQueries";
import { currentYear, DAY_KINDS, type DayKind, formatHours, monthName, monthWeeks } from "./types";
import { YearSelect } from "./YearSelect";

const route = getRouteApi("/calendar/");

/** Colours of a day by kind — HeroUI tokens only, so both themes work. */
export const DAY_KIND_CLASS: Record<DayKind, string> = {
	workday: "bg-surface",
	dayOff: "bg-surface-secondary text-muted",
	holiday: "bg-danger-soft text-danger-soft-foreground",
	designatedOff: "bg-warning-soft text-warning-soft-foreground",
};

const WEEKDAYS = ["mon", "tue", "wed", "thu", "fri", "sat", "sun"] as const;

/** "Kalendarz" (Kadry, Administrator): the company's working calendar for a year, generated once and then edited. */
export function CalendarPage() {
	const { t } = useTranslation();
	const search = route.useSearch();
	const navigate = route.useNavigate();
	const year = search.year ?? currentYear();
	const { data: companies } = useQuery(activeCompaniesQuery());
	// Without a choice in the address the first company is shown, so the page is never empty.
	const companyId = search.companyId ?? companies?.items[0]?.id ?? "";
	const dialog = useRef<CalendarDayDialogHandle>(null);

	return (
		<section className="flex flex-col gap-4">
			<header>
				<h1 className="text-2xl font-semibold">{t("timeAbsence.calendar.title")}</h1>
				<p className="max-w-3xl text-sm text-muted">{t("timeAbsence.calendar.hint")}</p>
			</header>
			<div className="flex flex-wrap items-end gap-4">
				<CompanySelect
					className="w-56"
					label={t("timeAbsence.calendar.company")}
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
				<CompanyCalendar
					companyId={companyId}
					year={year}
					onDay={(day) => dialog.current?.edit(companyId, day)}
				/>
			) : (
				<p className="text-sm text-muted">{t("timeAbsence.calendar.chooseCompany")}</p>
			)}
			<CalendarDayDialog ref={dialog} />
		</section>
	);
}

function CompanyCalendar({
	companyId,
	year,
	onDay,
}: {
	companyId: string;
	year: number;
	onDay: (day: GetCalendarMonthDay) => void;
}) {
	const { t } = useTranslation();
	const { data, isPending } = useQuery(calendarYearQuery(companyId, year));

	if (isPending) return <Spinner aria-label={t("common.loading")} />;
	if (!data || data.length === 0) return <GenerateCalendar companyId={companyId} year={year} />;

	const days = data.reduce((sum, month) => sum + month.workDays, 0);
	const hours = data.reduce((sum, month) => sum + month.hours, 0);
	return (
		<div className="flex flex-col gap-3">
			<div className="flex flex-wrap items-center gap-x-6 gap-y-2 text-sm">
				<span className="font-medium">
					{t("timeAbsence.calendar.yearTotal", { days, hours: formatHours(hours) })}
				</span>
				<span className="text-muted">{t("timeAbsence.calendar.legend")}:</span>
				{DAY_KINDS.map((kind) => (
					<span key={kind} className="flex items-center gap-1.5">
						<span
							className={`inline-block size-3 border border-border ${DAY_KIND_CLASS[kind]}`}
							aria-hidden
						/>
						{t(`timeAbsence.dayKinds.${kind}`)}
					</span>
				))}
			</div>
			<div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3 2xl:grid-cols-4">
				{data.map((month) => (
					<MonthCard
						key={month.number}
						companyId={companyId}
						year={year}
						month={month}
						onDay={onDay}
					/>
				))}
			</div>
		</div>
	);
}

function GenerateCalendar({ companyId, year }: { companyId: string; year: number }) {
	const { t } = useTranslation();
	const generate = useGenerateCalendar();
	const { data: holidays } = useQuery(holidaysQuery(year));
	const saturdays = (holidays ?? []).filter((holiday) => holiday.isSaturday);

	const onGenerate = async () => {
		try {
			const result = await generate.mutateAsync({ id: companyId, year });
			toast.success(
				result.saturdayHolidays > 0
					? t("timeAbsence.calendar.generatedDesignate", {
							year,
							count: result.saturdayHolidays,
						})
					: t("timeAbsence.calendar.generated", { year }),
			);
		} catch (error) {
			onActionError(t, error);
		}
	};

	return (
		<Card className="max-w-3xl">
			<Card.Header>
				<Card.Title>{t("timeAbsence.calendar.missing", { year })}</Card.Title>
				<Card.Description>{t("timeAbsence.calendar.generateHint")}</Card.Description>
			</Card.Header>
			<Card.Content className="flex flex-col gap-3">
				{holidays ? (
					saturdays.length > 0 ? (
						<Notice>
							{t("timeAbsence.calendar.saturdayHolidays", {
								year,
								count: saturdays.length,
								dates: saturdays.map((holiday) => `${holiday.date} ${holiday.name}`).join(", "),
							})}
						</Notice>
					) : (
						<p className="text-sm text-muted">
							{t("timeAbsence.calendar.noSaturdayHolidays", { year })}
						</p>
					)
				) : null}
				<div>
					<Button onPress={() => void onGenerate()} isPending={generate.isPending}>
						{t("timeAbsence.calendar.generate")}
					</Button>
				</div>
			</Card.Content>
		</Card>
	);
}

function MonthCard({
	companyId,
	year,
	month,
	onDay,
}: {
	companyId: string;
	year: number;
	month: GetCalendarYearMonth;
	onDay: (day: GetCalendarMonthDay) => void;
}) {
	const { t } = useTranslation();
	const { data } = useQuery(calendarMonthQuery(companyId, year, month.number));
	const weeks = monthWeeks(year, month.number, data ?? []);

	return (
		<Card className="gap-2 p-3">
			<div className="flex flex-wrap items-center justify-between gap-2">
				<h2 className="text-sm font-semibold">{monthName(month.number)}</h2>
				<span className="text-xs text-muted tabular-nums">
					{t("timeAbsence.calendar.monthTotal", {
						days: month.workDays,
						hours: formatHours(month.hours),
					})}
				</span>
			</div>
			{month.missingDesignated ? (
				<Chip size="sm" color="warning" className="self-start">
					{t("timeAbsence.calendar.missingDesignated")}
				</Chip>
			) : null}
			<table className="w-full table-fixed border-collapse text-xs tabular-nums">
				<thead>
					<tr>
						{WEEKDAYS.map((day) => (
							<th key={day} className="pb-1 text-center font-medium text-muted">
								{t(`timeAbsence.weekdays.${day}`)}
							</th>
						))}
					</tr>
				</thead>
				<tbody>
					{weeks.map((week) => (
						<tr key={week.find(Boolean)?.date}>
							{week.map((cell, index) =>
								cell ? (
									<td key={cell.date} className="p-px">
										{cell.data ? (
											<DayButton day={cell.data} number={cell.day} onPress={onDay} />
										) : (
											<span className="block h-9 text-center text-muted">{cell.day}</span>
										)}
									</td>
								) : (
									// biome-ignore lint/suspicious/noArrayIndexKey: empty places before and after the month have no identity
									<td key={`empty-${index}`} className="p-px" />
								),
							)}
						</tr>
					))}
				</tbody>
			</table>
		</Card>
	);
}

function DayButton({
	day,
	number,
	onPress,
}: {
	day: GetCalendarMonthDay;
	number: number;
	onPress: (day: GetCalendarMonthDay) => void;
}) {
	const { t } = useTranslation();
	const kind = day.kind as DayKind;
	const label = [
		day.date,
		t(`timeAbsence.dayKinds.${kind}`),
		day.holidayName,
		kind === "workday" ? formatHours(day.hours) : null,
	]
		.filter(Boolean)
		.join(", ");
	return (
		<button
			type="button"
			title={label}
			aria-label={label}
			onClick={() => onPress(day)}
			className={`flex h-9 w-full cursor-pointer flex-col items-center justify-center border border-border leading-tight hover:outline-2 hover:outline-accent focus-visible:outline-2 focus-visible:outline-accent ${DAY_KIND_CLASS[kind]}`}
		>
			<span className="font-medium">{number}</span>
			{kind === "workday" ? (
				<span className="text-[10px] text-muted">{day.hours.toString().replace(".", ",")}</span>
			) : null}
		</button>
	);
}
