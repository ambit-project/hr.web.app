import { Button, Spinner, toast } from "@heroui/react";
import { useQuery } from "@tanstack/react-query";
import { useRef } from "react";
import { useTranslation } from "react-i18next";
import type { ListConsentsItem } from "#/api/models";
import { AddButton, EditButton } from "#/components/ui/AddButton";
import { ConfirmDelete } from "#/components/ui/ConfirmDelete";
import { onActionError } from "#/forms/feedback";
import { ConsentDialog, type ConsentDialogHandle } from "./ConsentDialog";
import { ConsentsTable } from "./ConsentsTable";
import { EndDateDialog, type EndDateDialogHandle } from "./EndDateDialog";
import { consentsQuery, useDeleteConsent, useEndConsent } from "./payComponentsQueries";

/** "Zgody na potrącenia" on the employee page (Kadry, Administrator). */
export function ConsentsSection({ employeeId }: { employeeId: string }) {
	const { t } = useTranslation();
	const dialog = useRef<ConsentDialogHandle>(null);
	const endDialog = useRef<EndDateDialogHandle>(null);
	const end = useEndConsent();
	const { data, isPending } = useQuery(consentsQuery(employeeId));

	const onEnd = (item: ListConsentsItem) =>
		endDialog.current?.open({
			title: t("payComponents.consents.end.title", { name: item.name }),
			hint: t("payComponents.consents.end.hint"),
			validTo: item.validTo,
			savedMessage: t("payComponents.consents.end.saved"),
			undoneMessage: t("payComponents.consents.end.undone"),
			save: (validTo) =>
				end.mutateAsync({ id: item.id, request: { rowVersion: item.rowVersion, validTo } }),
		});

	return (
		<div className="flex flex-col gap-4">
			<h2 className="text-lg font-semibold">{t("payComponents.consents.title")}</h2>
			<p className="max-w-3xl text-sm text-muted">{t("payComponents.consents.hint")}</p>
			<div className="flex gap-2">
				<AddButton
					label={t("payComponents.consents.create.title")}
					onPress={() => dialog.current?.create(employeeId)}
				/>
			</div>
			{isPending ? (
				<Spinner aria-label={t("common.loading")} />
			) : (
				<ConsentsTable
					items={data ?? []}
					actions={(item) => (
						<div className="flex items-center gap-2">
							{item.signedAt === null ? (
								<EditButton
									size="sm"
									variant="ghost"
									label={t("payComponents.consents.edit.title", { name: item.name })}
									onPress={() => dialog.current?.edit(item)}
								/>
							) : (
								<Button size="sm" variant="tertiary" onPress={() => onEnd(item)}>
									{t("payComponents.consents.end.action")}
								</Button>
							)}
							<DeleteConsent item={item} />
						</div>
					)}
				/>
			)}
			<ConsentDialog ref={dialog} />
			<EndDateDialog ref={endDialog} />
		</div>
	);
}

function DeleteConsent({ item }: { item: ListConsentsItem }) {
	const { t } = useTranslation();
	const remove = useDeleteConsent();

	const onDelete = async () => {
		try {
			await remove.mutateAsync({ id: item.id, rowVersion: item.rowVersion });
			toast.success(t("payComponents.consents.delete.deleted"));
		} catch (error) {
			onActionError(t, error);
		}
	};

	return (
		<ConfirmDelete
			label={t("payComponents.consents.delete.action")}
			title={t("payComponents.consents.delete.title")}
			body={t("payComponents.consents.delete.body")}
			onConfirm={() => void onDelete()}
		/>
	);
}
