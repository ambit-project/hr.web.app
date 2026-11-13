import { Button, Chip, EmptyState, Spinner, Table, toast } from "@heroui/react";
import { useQuery } from "@tanstack/react-query";
import { useRef } from "react";
import { useTranslation } from "react-i18next";
import type { ListEmploymentComponentsItem } from "#/api/models";
import { AddButton } from "#/components/ui/AddButton";
import { ConfirmDelete } from "./ConfirmDelete";
import {
	type ComponentContract,
	EmploymentComponentDialog,
	type EmploymentComponentDialogHandle,
} from "./EmploymentComponentDialog";
import { EndDateDialog, type EndDateDialogHandle } from "./EndDateDialog";
import { onActionError } from "./feedback";
import {
	employmentComponentsQuery,
	useDeleteEmploymentComponent,
	useEndEmploymentComponent,
} from "./payComponentsQueries";
import {
	type ComponentKind,
	type ComponentStatus,
	formatComponentValue,
	formatPeriod,
} from "./types";

const COLORS: Record<ComponentStatus, "success" | "warning" | "default"> = {
	active: "success",
	future: "warning",
	ended: "default",
};

/** "Składniki" on the contract page: additions and deductions assigned to the contract for a period. */
export function EmploymentComponentsSection({ contract }: { contract: ComponentContract }) {
	const { t } = useTranslation();
	const addDialog = useRef<EmploymentComponentDialogHandle>(null);
	const endDialog = useRef<EndDateDialogHandle>(null);
	const end = useEndEmploymentComponent();
	const { data, isPending } = useQuery(employmentComponentsQuery(contract.id));

	const onEnd = (item: ListEmploymentComponentsItem) =>
		endDialog.current?.open({
			title: t("payComponents.components.end.title", { name: item.name }),
			hint: t("payComponents.components.end.hint"),
			validTo: item.validTo,
			savedMessage: t("payComponents.components.end.saved"),
			undoneMessage: t("payComponents.components.end.undone"),
			save: (validTo) =>
				end.mutateAsync({ id: item.id, request: { rowVersion: item.rowVersion, validTo } }),
		});

	return (
		<div className="flex flex-col gap-4">
			<h2 className="pt-4 text-lg font-semibold">{t("payComponents.components.title")}</h2>
			<p className="max-w-3xl text-sm text-muted">{t("payComponents.components.hint")}</p>
			<div className="flex gap-2">
				<AddButton
					label={t("payComponents.components.add.title")}
					onPress={() => addDialog.current?.add(contract)}
				/>
			</div>
			{isPending ? (
				<Spinner aria-label={t("common.loading")} />
			) : (
				<Table>
					<Table.ScrollContainer>
						<Table.Content
							aria-label={t("payComponents.components.title")}
							className="min-w-[820px]"
						>
							<Table.Header>
								<Table.Column isRowHeader className="w-24">
									{t("payComponents.fields.code")}
								</Table.Column>
								<Table.Column>{t("payComponents.fields.name")}</Table.Column>
								<Table.Column className="text-right">
									{t("payComponents.components.fields.value")}
								</Table.Column>
								<Table.Column>{t("payComponents.components.fields.period")}</Table.Column>
								<Table.Column>{t("payComponents.components.fields.status")}</Table.Column>
								<Table.Column>{t("common.actions")}</Table.Column>
							</Table.Header>
							<Table.Body
								renderEmptyState={() => (
									<EmptyState className="py-8 text-center text-sm text-muted">
										{t("payComponents.components.empty")}
									</EmptyState>
								)}
							>
								{(data ?? []).map((item) => (
									<Table.Row key={item.id} id={item.id}>
										<Table.Cell>
											<code>{item.code}</code>
										</Table.Cell>
										<Table.Cell>
											{item.name}
											<span className="block text-xs text-muted">
												{t(`payComponents.kinds.${item.kind as ComponentKind}`)}
												{item.consentId ? `, ${t("payComponents.components.consentBased")}` : ""}
											</span>
										</Table.Cell>
										<Table.Cell className="text-right whitespace-nowrap">
											{formatComponentValue(item.calculation, item.value)}
										</Table.Cell>
										<Table.Cell className="whitespace-nowrap">
											{formatPeriod(item.validFrom, item.validTo)}
										</Table.Cell>
										<Table.Cell>
											<Chip size="sm" color={COLORS[item.status as ComponentStatus]}>
												{t(`payComponents.components.status.${item.status as ComponentStatus}`)}
											</Chip>
										</Table.Cell>
										<Table.Cell>
											<div className="flex gap-2">
												<EndButton onPress={() => onEnd(item)} />
												<DeleteComponent item={item} />
											</div>
										</Table.Cell>
									</Table.Row>
								))}
							</Table.Body>
						</Table.Content>
					</Table.ScrollContainer>
				</Table>
			)}
			<EmploymentComponentDialog ref={addDialog} />
			<EndDateDialog ref={endDialog} />
		</div>
	);
}

function EndButton({ onPress }: { onPress: () => void }) {
	const { t } = useTranslation();
	return (
		<Button size="sm" variant="tertiary" onPress={onPress}>
			{t("payComponents.components.end.action")}
		</Button>
	);
}

function DeleteComponent({ item }: { item: ListEmploymentComponentsItem }) {
	const { t } = useTranslation();
	const remove = useDeleteEmploymentComponent();

	const onDelete = async () => {
		try {
			await remove.mutateAsync({ id: item.id, rowVersion: item.rowVersion });
			toast.success(t("payComponents.components.delete.deleted"));
		} catch (error) {
			onActionError(t, error);
		}
	};

	return (
		<ConfirmDelete
			label={t("payComponents.components.delete.action")}
			title={t("payComponents.components.delete.title")}
			body={t("payComponents.components.delete.body")}
			onConfirm={() => void onDelete()}
		/>
	);
}
