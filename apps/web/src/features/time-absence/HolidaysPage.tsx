import { Chip, EmptyState, Spinner, Table, toast } from "@heroui/react";
import { useQuery } from "@tanstack/react-query";
import { getRouteApi } from "@tanstack/react-router";
import { useRef } from "react";
import { useTranslation } from "react-i18next";
import type { ListHolidaysItem } from "#/api/models";
import { AddButton } from "#/components/ui/AddButton";
import { ConfirmDelete } from "#/components/ui/ConfirmDelete";
import { hasRole } from "#/features/auth/session";
import { onActionError } from "#/forms/feedback";
import { HolidayDialog, type HolidayDialogHandle } from "./HolidayDialog";
import { holidaysQuery, useDeleteHoliday } from "./timeAbsenceQueries";
import { currentYear, isoWeekday } from "./types";
import { YearSelect } from "./YearSelect";

const route = getRouteApi("/holidays/");

const WEEKDAYS = ["mon", "tue", "wed", "thu", "fri", "sat", "sun"] as const;

/** "Święta": the statutory holidays dictionary by year; Kadry read, the Administrator adds and deletes. */
export function HolidaysPage() {
	const { t } = useTranslation();
	const { me } = route.useRouteContext();
	const search = route.useSearch();
	const navigate = route.useNavigate();
	const year = search.year ?? currentYear();
	const canEdit = hasRole(me, "administrator");
	const dialog = useRef<HolidayDialogHandle>(null);
	const { data, isPending } = useQuery(holidaysQuery(year));

	return (
		<section className="flex flex-col gap-4">
			<header className="flex flex-wrap items-center justify-between gap-3">
				<div>
					<h1 className="text-2xl font-semibold">{t("timeAbsence.holidays.title")}</h1>
					<p className="max-w-3xl text-sm text-muted">{t("timeAbsence.holidays.hint")}</p>
				</div>
				{canEdit ? (
					<AddButton
						label={t("timeAbsence.holidays.add.title")}
						onPress={() => dialog.current?.create(year)}
					/>
				) : null}
			</header>
			<YearSelect
				value={year}
				onChange={(value) =>
					void navigate({ search: (prev) => ({ ...prev, year: value }), replace: true })
				}
			/>
			{isPending ? (
				<Spinner aria-label={t("common.loading")} />
			) : (
				<Table className="max-w-3xl">
					<Table.ScrollContainer>
						<Table.Content aria-label={t("timeAbsence.holidays.title")} className="min-w-[560px]">
							<Table.Header>
								<Table.Column isRowHeader>{t("timeAbsence.holidays.date")}</Table.Column>
								<Table.Column>{t("timeAbsence.holidays.weekday")}</Table.Column>
								<Table.Column>{t("timeAbsence.holidays.name")}</Table.Column>
								{canEdit ? <Table.Column>{t("common.actions")}</Table.Column> : null}
							</Table.Header>
							<Table.Body
								renderEmptyState={() => (
									<EmptyState className="py-8 text-center text-sm text-muted">
										{t("timeAbsence.holidays.empty")}
									</EmptyState>
								)}
							>
								{(data ?? []).map((item) => (
									<Table.Row key={item.date} id={item.date}>
										<Table.Cell>{item.date}</Table.Cell>
										<Table.Cell>
											{t(`timeAbsence.weekdays.${WEEKDAYS[isoWeekday(item.date) - 1]}`)}
											{item.isSaturday ? (
												<Chip size="sm" color="warning" className="ml-2">
													{t("timeAbsence.holidays.saturday")}
												</Chip>
											) : null}
										</Table.Cell>
										<Table.Cell>{item.name}</Table.Cell>
										{canEdit ? (
											<Table.Cell>
												<DeleteHoliday item={item} />
											</Table.Cell>
										) : null}
									</Table.Row>
								))}
							</Table.Body>
						</Table.Content>
					</Table.ScrollContainer>
				</Table>
			)}
			<HolidayDialog ref={dialog} />
		</section>
	);
}

function DeleteHoliday({ item }: { item: ListHolidaysItem }) {
	const { t } = useTranslation();
	const remove = useDeleteHoliday();

	const onDelete = async () => {
		try {
			await remove.mutateAsync(item.date);
			toast.success(t("timeAbsence.holidays.delete.deleted"));
		} catch (error) {
			onActionError(t, error);
		}
	};

	return (
		<ConfirmDelete
			label={t("timeAbsence.holidays.delete.action")}
			title={t("timeAbsence.holidays.delete.title", { date: item.date })}
			body={t("timeAbsence.holidays.delete.body")}
			onConfirm={() => void onDelete()}
		/>
	);
}
