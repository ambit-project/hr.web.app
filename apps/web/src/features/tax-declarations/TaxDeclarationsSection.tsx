import { AlertDialog, Button, Spinner, toast } from "@heroui/react";
import { useQuery } from "@tanstack/react-query";
import { useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import type { ListTaxDeclarationsItem } from "#/api/models";
import { ToggleFilter } from "#/components/table/ToggleFilter";
import { ProblemError } from "#/lib/problem";
import { TaxDeclarationDrawer, type TaxDeclarationDrawerHandle } from "./TaxDeclarationDrawer";
import { TaxDeclarationsTable } from "./TaxDeclarationsTable";
import { taxDeclarationsQuery, useDeleteTaxDeclaration } from "./taxDeclarationsQueries";

/** Tax declarations tab of the employee page (Kadry, Administrator). */
export function TaxDeclarationsSection({ employeeId }: { employeeId: string }) {
	const { t } = useTranslation();
	const drawer = useRef<TaxDeclarationDrawerHandle>(null);
	const [onlyActive, setOnlyActive] = useState(false);
	const { data, isPending } = useQuery(taxDeclarationsQuery(employeeId, onlyActive));

	return (
		<div className="flex flex-col gap-4">
			<p className="max-w-3xl text-sm text-muted">{t("taxDeclarations.hint")}</p>
			<div className="flex flex-wrap items-center gap-4">
				<Button onPress={() => drawer.current?.create(employeeId)}>
					{t("taxDeclarations.create.title")}
				</Button>
				<ToggleFilter
					label={t("taxDeclarations.onlyActive")}
					value={onlyActive}
					onChange={setOnlyActive}
				/>
			</div>
			{isPending ? (
				<Spinner aria-label={t("common.loading")} />
			) : (
				<TaxDeclarationsTable
					items={data ?? []}
					actions={(item) => (
						<div className="flex gap-2">
							<Button size="sm" variant="tertiary" onPress={() => drawer.current?.end(item)}>
								{t("taxDeclarations.end.action")}
							</Button>
							<DeleteButton item={item} />
						</div>
					)}
				/>
			)}
			<TaxDeclarationDrawer ref={drawer} />
		</div>
	);
}

function DeleteButton({ item }: { item: ListTaxDeclarationsItem }) {
	const { t } = useTranslation();
	const remove = useDeleteTaxDeclaration();

	const onDelete = async () => {
		try {
			await remove.mutateAsync({ id: item.id, rowVersion: item.rowVersion });
			toast.success(t("taxDeclarations.delete.deleted"));
		} catch (error) {
			if (error instanceof ProblemError && error.problem.status === 409) {
				toast.warning(t("common.conflict"));
			} else {
				toast.danger(t("common.serverError"));
			}
		}
	};

	return (
		<AlertDialog>
			<Button size="sm" variant="danger-soft">
				{t("taxDeclarations.delete.action")}
			</Button>
			<AlertDialog.Backdrop>
				<AlertDialog.Container>
					<AlertDialog.Dialog className="sm:max-w-[420px]">
						<AlertDialog.Header>
							<AlertDialog.Icon status="danger" />
							<AlertDialog.Heading>{t("taxDeclarations.delete.title")}</AlertDialog.Heading>
						</AlertDialog.Header>
						<AlertDialog.Body>
							<p>{t("taxDeclarations.delete.body")}</p>
						</AlertDialog.Body>
						<AlertDialog.Footer>
							<Button slot="close" variant="tertiary">
								{t("common.cancel")}
							</Button>
							<Button slot="close" variant="danger" onPress={() => void onDelete()}>
								{t("taxDeclarations.delete.action")}
							</Button>
						</AlertDialog.Footer>
					</AlertDialog.Dialog>
				</AlertDialog.Container>
			</AlertDialog.Backdrop>
		</AlertDialog>
	);
}
