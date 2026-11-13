import { Chip, EmptyState, Spinner, Table, toast } from "@heroui/react";
import { useQuery } from "@tanstack/react-query";
import { useRef } from "react";
import { useTranslation } from "react-i18next";
import type { ListMandateHoursItem } from "#/api/models";
import { AddButton, EditButton } from "#/components/ui/AddButton";
import { ConfirmDelete } from "#/components/ui/ConfirmDelete";
import { onActionError, problemMessage } from "#/forms/feedback";
import { MandateHoursDialog, type MandateHoursDialogHandle } from "./MandateHoursDialog";
import { Notice } from "./Notice";
import { mandateHoursQuery, useDeleteMandateHours } from "./timeAbsenceQueries";
import { type ConfirmationForm, formatHours } from "./types";

/** "Godziny zlecenia" of a mandate paid by the hour; an unconfirmed entry is kept but payroll skips it. */
export function MandateHoursSection({
	employmentId,
	year,
}: {
	employmentId: string;
	year: number;
}) {
	const { t } = useTranslation();
	const dialog = useRef<MandateHoursDialogHandle>(null);
	const { data, isPending, error } = useQuery(mandateHoursQuery(employmentId, year));

	return (
		<div className="flex flex-col gap-3">
			<h3 className="text-base font-semibold">{t("timeAbsence.mandateHours.title")}</h3>
			<p className="max-w-3xl text-sm text-muted">{t("timeAbsence.mandateHours.hint")}</p>
			<div className="flex gap-2">
				<AddButton
					label={t("timeAbsence.mandateHours.create.title")}
					onPress={() => dialog.current?.create(employmentId)}
				/>
			</div>
			{isPending ? (
				<Spinner aria-label={t("common.loading")} />
			) : error || !data ? (
				<Notice status="danger">{problemMessage(t, error)}</Notice>
			) : (
				<Table className="max-w-4xl">
					<Table.ScrollContainer>
						<Table.Content
							aria-label={t("timeAbsence.mandateHours.title")}
							className="min-w-[640px]"
						>
							<Table.Header>
								<Table.Column isRowHeader>
									{t("timeAbsence.mandateHours.columns.period")}
								</Table.Column>
								<Table.Column className="text-right">
									{t("timeAbsence.mandateHours.fields.hours")}
								</Table.Column>
								<Table.Column>{t("timeAbsence.mandateHours.columns.confirmation")}</Table.Column>
								<Table.Column>{t("common.actions")}</Table.Column>
							</Table.Header>
							<Table.Body
								renderEmptyState={() => (
									<EmptyState className="py-8 text-center text-sm text-muted">
										{t("timeAbsence.mandateHours.empty")}
									</EmptyState>
								)}
							>
								{data.map((item) => (
									<Table.Row key={item.id} id={item.id}>
										<Table.Cell className="whitespace-nowrap">
											{item.dateFrom} – {item.dateTo}
										</Table.Cell>
										<Table.Cell className="text-right">{formatHours(item.hours)}</Table.Cell>
										<Table.Cell>
											{item.confirmed && item.confirmedAt && item.confirmationForm ? (
												`${item.confirmedAt}, ${t(
													`timeAbsence.mandateHours.forms.${item.confirmationForm as ConfirmationForm}`,
												)}`
											) : (
												<Chip size="sm" color="warning">
													{t("timeAbsence.mandateHours.notConfirmed")}
												</Chip>
											)}
										</Table.Cell>
										<Table.Cell>
											<div className="flex items-center gap-2">
												<EditButton
													size="sm"
													variant="ghost"
													label={t("timeAbsence.mandateHours.edit.title", {
														period: `${item.dateFrom} – ${item.dateTo}`,
													})}
													onPress={() => dialog.current?.edit(item)}
												/>
												<DeleteMandateHours item={item} />
											</div>
										</Table.Cell>
									</Table.Row>
								))}
							</Table.Body>
						</Table.Content>
					</Table.ScrollContainer>
				</Table>
			)}
			<MandateHoursDialog ref={dialog} />
		</div>
	);
}

function DeleteMandateHours({ item }: { item: ListMandateHoursItem }) {
	const { t } = useTranslation();
	const remove = useDeleteMandateHours();

	const onDelete = async () => {
		try {
			await remove.mutateAsync({ id: item.id, rowVersion: item.rowVersion });
			toast.success(t("timeAbsence.mandateHours.delete.deleted"));
		} catch (error) {
			onActionError(t, error);
		}
	};

	return (
		<ConfirmDelete
			label={t("timeAbsence.mandateHours.delete.action")}
			title={t("timeAbsence.mandateHours.delete.title")}
			body={t("timeAbsence.mandateHours.delete.body", {
				period: `${item.dateFrom} – ${item.dateTo}`,
			})}
			onConfirm={() => void onDelete()}
		/>
	);
}
