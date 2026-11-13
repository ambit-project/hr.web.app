import { EmptyState, Spinner, Table, toast } from "@heroui/react";
import { useQuery } from "@tanstack/react-query";
import { useRef } from "react";
import { useTranslation } from "react-i18next";
import type { ListAbsencesItem } from "#/api/models";
import { AddButton, EditButton } from "#/components/ui/AddButton";
import { ConfirmDelete } from "#/components/ui/ConfirmDelete";
import { onActionError, problemMessage } from "#/forms/feedback";
import { AbsenceDialog, type AbsenceDialogHandle } from "./AbsenceDialog";
import { Notice } from "./Notice";
import { absencesQuery, absenceTypesQuery, useDeleteAbsence } from "./timeAbsenceQueries";
import { allowedAbsenceTypes, formatHours, type TimeContract } from "./types";

/** "Nieobecności" on the contract page: absences overlapping the chosen year, with days and hours from the calendar. */
export function AbsencesSection({ contract, year }: { contract: TimeContract; year: number }) {
	const { t } = useTranslation();
	const dialog = useRef<AbsenceDialogHandle>(null);
	const { data, isPending, error } = useQuery(absencesQuery(contract.id, year));
	const { data: allTypes } = useQuery(absenceTypesQuery());
	const types = allowedAbsenceTypes(allTypes ?? [], contract.kind, contract.hasSicknessInsurance);
	const withHours = contract.kind === "employment";

	return (
		<div className="flex flex-col gap-3">
			<h3 className="text-base font-semibold">{t("timeAbsence.absences.title")}</h3>
			{allTypes && types.length === 0 ? (
				<p className="max-w-3xl text-sm text-muted">{t("timeAbsence.absences.noTypes")}</p>
			) : (
				<div className="flex gap-2">
					<AddButton
						label={t("timeAbsence.absences.create.title")}
						isDisabled={!allTypes}
						onPress={() => dialog.current?.create(contract.id, types)}
					/>
				</div>
			)}
			{isPending ? (
				<Spinner aria-label={t("common.loading")} />
			) : error ? (
				<Notice status="danger">{problemMessage(t, error)}</Notice>
			) : (
				<Table>
					<Table.ScrollContainer>
						<Table.Content aria-label={t("timeAbsence.absences.title")} className="min-w-[900px]">
							<Table.Header>
								<Table.Column isRowHeader>{t("timeAbsence.absences.fields.type")}</Table.Column>
								<Table.Column>{t("timeAbsence.absences.columns.period")}</Table.Column>
								<Table.Column className="text-right">
									{t("timeAbsence.absences.columns.calendarDays")}
								</Table.Column>
								{withHours ? (
									<Table.Column className="text-right">
										{t("timeAbsence.absences.columns.workHours")}
									</Table.Column>
								) : null}
								<Table.Column>{t("timeAbsence.absences.fields.certificateNumber")}</Table.Column>
								<Table.Column>{t("timeAbsence.absences.fields.note")}</Table.Column>
								<Table.Column>{t("common.actions")}</Table.Column>
							</Table.Header>
							<Table.Body
								renderEmptyState={() => (
									<EmptyState className="py-8 text-center text-sm text-muted">
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
										{withHours ? (
											<Table.Cell className="text-right whitespace-nowrap">
												{formatHours(item.workHours)}
											</Table.Cell>
										) : null}
										<Table.Cell>{item.certificateNumber ?? "—"}</Table.Cell>
										<Table.Cell>{item.note ?? ""}</Table.Cell>
										<Table.Cell>
											<div className="flex items-center gap-2">
												<EditButton
													size="sm"
													variant="ghost"
													label={t("timeAbsence.absences.edit.title", {
														type: item.absenceTypeName,
													})}
													isDisabled={!allTypes}
													onPress={() =>
														dialog.current?.edit(
															item,
															// The saved type stays selectable even when the contract no longer allows it.
															types.some((type) => type.code === item.absenceTypeId)
																? types
																: (allTypes ?? []).filter(
																		(type) =>
																			type.code === item.absenceTypeId || types.includes(type),
																	),
														)
													}
												/>
												<DeleteAbsence item={item} />
											</div>
										</Table.Cell>
									</Table.Row>
								))}
							</Table.Body>
						</Table.Content>
					</Table.ScrollContainer>
				</Table>
			)}
			<AbsenceDialog ref={dialog} />
		</div>
	);
}

function DeleteAbsence({ item }: { item: ListAbsencesItem }) {
	const { t } = useTranslation();
	const remove = useDeleteAbsence();

	const onDelete = async () => {
		try {
			await remove.mutateAsync({ id: item.id, rowVersion: item.rowVersion });
			toast.success(t("timeAbsence.absences.delete.deleted"));
		} catch (error) {
			onActionError(t, error);
		}
	};

	return (
		<ConfirmDelete
			label={t("timeAbsence.absences.delete.action")}
			title={t("timeAbsence.absences.delete.title")}
			body={t("timeAbsence.absences.delete.body", {
				type: item.absenceTypeName,
				from: item.dateFrom,
				to: item.dateTo,
			})}
			onConfirm={() => void onDelete()}
		/>
	);
}
