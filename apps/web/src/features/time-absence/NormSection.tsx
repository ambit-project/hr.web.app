import { Chip, Spinner, Table } from "@heroui/react";
import { useQuery } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import { problemMessage } from "#/forms/feedback";
import { Notice } from "./Notice";
import { normQuery } from "./timeAbsenceQueries";
import { formatHours, monthName } from "./types";

/** "Wymiar czasu pracy" of an employment contract: hours of each month of the year (calendar × FTE). */
export function NormSection({ employmentId, year }: { employmentId: string; year: number }) {
	const { t } = useTranslation();
	const { data, isPending, error } = useQuery(normQuery(employmentId, year));
	const total = (data ?? []).reduce(
		(sum, month) => ({
			workDays: sum.workDays + month.workDays,
			calendarHours: sum.calendarHours + month.calendarHours,
			normHours: sum.normHours + month.normHours,
		}),
		{ workDays: 0, calendarHours: 0, normHours: 0 },
	);

	return (
		<div className="flex flex-col gap-3">
			<h3 className="text-base font-semibold">{t("timeAbsence.norm.title")}</h3>
			<p className="max-w-3xl text-sm text-muted">{t("timeAbsence.norm.hint")}</p>
			{isPending ? (
				<Spinner aria-label={t("common.loading")} />
			) : error || !data ? (
				<Notice status="danger">{problemMessage(t, error)}</Notice>
			) : (
				<Table className="max-w-3xl">
					<Table.ScrollContainer>
						<Table.Content aria-label={t("timeAbsence.norm.title")} className="min-w-[560px]">
							<Table.Header>
								<Table.Column isRowHeader>{t("timeAbsence.norm.month")}</Table.Column>
								<Table.Column className="text-right">{t("timeAbsence.norm.workDays")}</Table.Column>
								<Table.Column className="text-right">
									{t("timeAbsence.norm.calendarHours")}
								</Table.Column>
								<Table.Column className="text-right">
									{t("timeAbsence.norm.normHours")}
								</Table.Column>
							</Table.Header>
							<Table.Body>
								{[
									...data.map((month) => (
										<Table.Row key={month.number} id={month.number}>
											<Table.Cell>
												{monthName(month.number)}
												{month.missingDesignated ? (
													<Chip
														size="sm"
														color="warning"
														className="ml-2"
														title={t("timeAbsence.calendar.missingDesignatedHint")}
													>
														{t("timeAbsence.calendar.missingDesignated")}
													</Chip>
												) : null}
											</Table.Cell>
											<Table.Cell className="text-right">{month.workDays}</Table.Cell>
											<Table.Cell className="text-right">
												{formatHours(month.calendarHours)}
											</Table.Cell>
											<Table.Cell className="text-right">{formatHours(month.normHours)}</Table.Cell>
										</Table.Row>
									)),
									<Table.Row key="total" id="total">
										<Table.Cell className="font-semibold">{t("timeAbsence.norm.total")}</Table.Cell>
										<Table.Cell className="text-right font-semibold">{total.workDays}</Table.Cell>
										<Table.Cell className="text-right font-semibold">
											{formatHours(total.calendarHours)}
										</Table.Cell>
										<Table.Cell className="text-right font-semibold">
											{formatHours(total.normHours)}
										</Table.Cell>
									</Table.Row>,
								]}
							</Table.Body>
						</Table.Content>
					</Table.ScrollContainer>
				</Table>
			)}
		</div>
	);
}
