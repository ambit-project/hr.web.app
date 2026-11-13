import { Button, Chip, EmptyState, Spinner, Table, toast } from "@heroui/react";
import { useQuery } from "@tanstack/react-query";
import { useRef } from "react";
import { useTranslation } from "react-i18next";
import type { ListGarnishmentsItem } from "#/api/models";
import { AddButton, EditButton } from "#/components/ui/AddButton";
import { ConfirmDelete } from "#/components/ui/ConfirmDelete";
import { onActionError } from "#/forms/feedback";
import { formatMoney } from "#/lib/money";
import { GarnishmentDialog, type GarnishmentDialogHandle } from "./GarnishmentDialog";
import { garnishmentsQuery, useDeleteGarnishment } from "./payComponentsQueries";
import type { AuthorityKind, GarnishmentKind, GarnishmentStatus } from "./types";

const COLORS: Record<GarnishmentStatus, "success" | "default" | "warning"> = {
	active: "warning",
	paidOff: "success",
	discontinued: "default",
};

const money = (amount: number | null) => (amount === null ? "—" : formatMoney(amount));

/** "Zajęcia wynagrodzenia" on the employee page (Kadry, Administrator); reading them is audited. */
export function GarnishmentsSection({ employeeId }: { employeeId: string }) {
	const { t } = useTranslation();
	const dialog = useRef<GarnishmentDialogHandle>(null);
	const { data, isPending } = useQuery(garnishmentsQuery(employeeId));

	return (
		<div className="flex flex-col gap-4">
			<h2 className="text-lg font-semibold">{t("payComponents.garnishments.title")}</h2>
			<p className="max-w-3xl text-sm text-muted">{t("payComponents.garnishments.hint")}</p>
			<div className="flex gap-2">
				<AddButton
					label={t("payComponents.garnishments.create.title")}
					onPress={() => dialog.current?.create(employeeId)}
				/>
			</div>
			{isPending ? (
				<Spinner aria-label={t("common.loading")} />
			) : (
				<Table>
					<Table.ScrollContainer>
						<Table.Content
							aria-label={t("payComponents.garnishments.title")}
							className="min-w-[1080px]"
						>
							<Table.Header>
								<Table.Column isRowHeader>
									{t("payComponents.garnishments.fields.kind")}
								</Table.Column>
								<Table.Column>{t("payComponents.garnishments.columns.authority")}</Table.Column>
								<Table.Column>{t("payComponents.garnishments.fields.caseNumber")}</Table.Column>
								<Table.Column>{t("payComponents.garnishments.columns.receivedAt")}</Table.Column>
								<Table.Column className="text-right">
									{t("payComponents.garnishments.columns.monthlyAmount")}
								</Table.Column>
								<Table.Column className="text-right">
									{t("payComponents.garnishments.columns.totalAmount")}
								</Table.Column>
								<Table.Column className="text-right">
									{t("payComponents.garnishments.columns.collectedAmount")}
								</Table.Column>
								<Table.Column>{t("payComponents.garnishments.fields.status")}</Table.Column>
								<Table.Column>{t("common.actions")}</Table.Column>
							</Table.Header>
							<Table.Body
								renderEmptyState={() => (
									<EmptyState className="py-8 text-center text-sm text-muted">
										{t("payComponents.garnishments.empty")}
									</EmptyState>
								)}
							>
								{(data ?? []).map((item) => (
									<Table.Row key={item.id} id={item.id}>
										<Table.Cell>
											{t(`payComponents.garnishments.kinds.${item.kind as GarnishmentKind}`)}
										</Table.Cell>
										<Table.Cell>
											{item.authorityName}
											<span className="block text-xs text-muted">
												{t(
													`payComponents.garnishments.authorityKinds.${item.authorityKind as AuthorityKind}`,
												)}
											</span>
										</Table.Cell>
										<Table.Cell>
											{item.caseNumber}
											{item.note ? (
												<span className="block text-xs text-muted">{item.note}</span>
											) : null}
										</Table.Cell>
										<Table.Cell>{item.receivedAt}</Table.Cell>
										<Table.Cell className="text-right whitespace-nowrap">
											{money(item.monthlyAmount)}
										</Table.Cell>
										<Table.Cell className="text-right whitespace-nowrap">
											{money(item.totalAmount)}
										</Table.Cell>
										<Table.Cell className="text-right whitespace-nowrap">
											{formatMoney(item.collectedAmount)}
										</Table.Cell>
										<Table.Cell>
											<Chip size="sm" color={COLORS[item.status as GarnishmentStatus]}>
												{t(`payComponents.garnishments.status.${item.status as GarnishmentStatus}`)}
											</Chip>
											{item.statusDate ? (
												<span className="block text-xs text-muted">{item.statusDate}</span>
											) : null}
										</Table.Cell>
										<Table.Cell>
											<div className="flex items-center gap-2">
												<EditButton
													size="sm"
													variant="ghost"
													label={t("payComponents.garnishments.edit.title", {
														caseNumber: item.caseNumber,
													})}
													onPress={() => dialog.current?.edit(item)}
												/>
												<Button
													size="sm"
													variant="tertiary"
													onPress={() => dialog.current?.setStatus(item)}
												>
													{t("payComponents.garnishments.setStatus.action")}
												</Button>
												<DeleteGarnishment item={item} />
											</div>
										</Table.Cell>
									</Table.Row>
								))}
							</Table.Body>
						</Table.Content>
					</Table.ScrollContainer>
				</Table>
			)}
			<GarnishmentDialog ref={dialog} />
		</div>
	);
}

function DeleteGarnishment({ item }: { item: ListGarnishmentsItem }) {
	const { t } = useTranslation();
	const remove = useDeleteGarnishment();

	const onDelete = async () => {
		try {
			await remove.mutateAsync({ id: item.id, rowVersion: item.rowVersion });
			toast.success(t("payComponents.garnishments.delete.deleted"));
		} catch (error) {
			onActionError(t, error);
		}
	};

	return (
		<ConfirmDelete
			label={t("payComponents.garnishments.delete.action")}
			title={t("payComponents.garnishments.delete.title")}
			body={t("payComponents.garnishments.delete.body")}
			onConfirm={() => void onDelete()}
		/>
	);
}
