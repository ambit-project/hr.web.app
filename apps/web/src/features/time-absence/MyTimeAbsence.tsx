import { EmptyState, Spinner, Table } from "@heroui/react";
import { useQuery } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import { myAbsencesQuery, myLeaveQuery } from "./timeAbsenceQueries";
import { formatHours, formatNumber, hoursToDays } from "./types";

/** Leave balance of the person's own employment contracts in a year ("Moje dokumenty", read only). */
export function MyLeaveBalance({ employeeId, year }: { employeeId: string; year: number }) {
	const { t } = useTranslation();
	const { data, isPending } = useQuery(myLeaveQuery(employeeId, year));
	const hoursAndDays = (hours: number, days = hoursToDays(hours)) =>
		t("timeAbsence.leave.hoursAndDays", { hours: formatHours(hours), days: formatNumber(days) });

	if (isPending) return <Spinner aria-label={t("common.loading")} />;
	if (!data || data.length === 0) return <p className="text-sm text-muted">{t("my.noLeave")}</p>;
	return (
		<Table>
			<Table.ScrollContainer>
				<Table.Content aria-label={t("my.leave", { year })} className="min-w-[720px]">
					<Table.Header>
						<Table.Column isRowHeader>{t("employments.fields.number")}</Table.Column>
						<Table.Column className="text-right">{t("timeAbsence.leave.entitled")}</Table.Column>
						<Table.Column className="text-right">{t("timeAbsence.leave.carried")}</Table.Column>
						<Table.Column className="text-right">{t("timeAbsence.leave.used")}</Table.Column>
						<Table.Column className="text-right">{t("timeAbsence.leave.balance")}</Table.Column>
					</Table.Header>
					<Table.Body>
						{data.map((item) => (
							<Table.Row key={item.employmentId} id={item.employmentId}>
								<Table.Cell>{item.employmentNumber}</Table.Cell>
								<Table.Cell className="text-right">{hoursAndDays(item.entitledHours)}</Table.Cell>
								<Table.Cell className="text-right">
									{hoursAndDays(item.carriedOverHours)}
								</Table.Cell>
								<Table.Cell className="text-right">{hoursAndDays(item.usedHours)}</Table.Cell>
								<Table.Cell className="text-right font-semibold">
									{hoursAndDays(item.balanceHours, item.balanceDays)}
								</Table.Cell>
							</Table.Row>
						))}
					</Table.Body>
				</Table.Content>
			</Table.ScrollContainer>
		</Table>
	);
}

/** The person's own absences in a year ("Moje dokumenty", read only). */
export function MyAbsences({ employeeId, year }: { employeeId: string; year: number }) {
	const { t } = useTranslation();
	const { data, isPending } = useQuery(myAbsencesQuery(employeeId, year));

	if (isPending) return <Spinner aria-label={t("common.loading")} />;
	return (
		<Table>
			<Table.ScrollContainer>
				<Table.Content aria-label={t("my.absences", { year })} className="min-w-[720px]">
					<Table.Header>
						<Table.Column isRowHeader>{t("timeAbsence.absences.fields.type")}</Table.Column>
						<Table.Column>{t("timeAbsence.absences.columns.period")}</Table.Column>
						<Table.Column className="text-right">
							{t("timeAbsence.absences.columns.calendarDays")}
						</Table.Column>
						<Table.Column className="text-right">
							{t("timeAbsence.absences.columns.workHours")}
						</Table.Column>
						<Table.Column>{t("employments.fields.number")}</Table.Column>
					</Table.Header>
					<Table.Body
						renderEmptyState={() => (
							<EmptyState className="py-6 text-center text-sm text-muted">
								{t("timeAbsence.absences.empty")}
							</EmptyState>
						)}
					>
						{(data ?? []).map((item) => (
							<Table.Row key={item.id} id={item.id}>
								<Table.Cell>
									<code>{item.absenceTypeId}</code> {item.absenceTypeName}
								</Table.Cell>
								<Table.Cell className="whitespace-nowrap">
									{item.dateFrom} – {item.dateTo}
								</Table.Cell>
								<Table.Cell className="text-right">{item.calendarDays}</Table.Cell>
								<Table.Cell className="text-right">{formatHours(item.workHours)}</Table.Cell>
								<Table.Cell>{item.employmentNumber}</Table.Cell>
							</Table.Row>
						))}
					</Table.Body>
				</Table.Content>
			</Table.ScrollContainer>
		</Table>
	);
}
