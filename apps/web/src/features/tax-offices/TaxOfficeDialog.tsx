import { Button, Modal, toast } from "@heroui/react";
import { forwardRef, useId, useImperativeHandle, useState } from "react";
import { useTranslation } from "react-i18next";
import { z } from "zod";
import type { ListTaxOfficesItem } from "#/api/models";
import { text, useAppForm } from "#/forms";
import { applyFieldErrors } from "#/forms/serverErrors";
import { ProblemError } from "#/lib/problem";
import { useCreateTaxOffice, useUpdateTaxOffice } from "./taxOfficesQueries";

export type TaxOfficeDialogHandle = {
	create: () => void;
	edit: (office: ListTaxOfficesItem) => void;
};

type Target = { mode: "create" } | { mode: "edit"; office: ListTaxOfficesItem };

const schema = z.object({
	code: text({ required: true, max: 4 }).regex(/^\s*\d{4}\s*$/, "invalid"),
	name: text({ required: true, max: 300 }),
	isActive: z.boolean(),
});

const FIELDS = ["code", "name"] as const;

export const TaxOfficeDialog = forwardRef<TaxOfficeDialogHandle>(
	function TaxOfficeDialog(_props, ref) {
		const [target, setTarget] = useState<Target | null>(null);
		useImperativeHandle(
			ref,
			() => ({
				create: () => setTarget({ mode: "create" }),
				edit: (office) => setTarget({ mode: "edit", office }),
			}),
			[],
		);

		return (
			<Modal>
				<Modal.Backdrop
					isDismissable={false}
					isKeyboardDismissDisabled
					isOpen={target !== null}
					onOpenChange={(open) => !open && setTarget(null)}
				>
					<Modal.Container scroll="inside">
						{target ? <TaxOfficeForm target={target} onDone={() => setTarget(null)} /> : null}
					</Modal.Container>
				</Modal.Backdrop>
			</Modal>
		);
	},
);

function TaxOfficeForm({ target, onDone }: { target: Target; onDone: () => void }) {
	const { t } = useTranslation();
	const formId = useId();
	const create = useCreateTaxOffice();
	const update = useUpdateTaxOffice();
	const office = target.mode === "edit" ? target.office : null;

	const form = useAppForm({
		defaultValues: {
			code: office?.code ?? "",
			name: office?.name ?? "",
			isActive: office?.isActive ?? true,
		},
		validators: { onChange: schema },
		onSubmit: async ({ value, formApi }) => {
			try {
				if (office) {
					await update.mutateAsync({
						id: office.id,
						request: { rowVersion: office.rowVersion, name: value.name, isActive: value.isActive },
					});
					toast.success(t("taxOffices.edit.saved", { code: office.code }));
				} else {
					await create.mutateAsync({ code: value.code.trim(), name: value.name });
					toast.success(t("taxOffices.create.created", { code: value.code.trim() }));
				}
				onDone();
			} catch (error) {
				if (error instanceof ProblemError && error.problem.status === 409) {
					toast.warning(t("common.conflict"));
					onDone();
				} else if (
					!(error instanceof ProblemError) ||
					!applyFieldErrors(formApi, error.problem.errors, FIELDS)
				) {
					toast.danger(t("common.serverError"));
				}
			}
		},
	});

	return (
		<Modal.Dialog className="form-dialog">
			<Modal.Header>
				<Modal.Heading>
					{office
						? t("taxOffices.edit.title", { code: office.code })
						: t("taxOffices.create.title")}
				</Modal.Heading>
			</Modal.Header>
			<Modal.Body>
				<form
					id={formId}
					className="flex flex-col gap-4"
					noValidate
					onSubmit={(event) => {
						event.preventDefault();
						void form.handleSubmit();
					}}
				>
					{office ? null : (
						<form.AppField name="code">
							{(field) => <field.TextInput label={t("taxOffices.fields.code")} isRequired />}
						</form.AppField>
					)}
					<form.AppField name="name">
						{(field) => <field.TextInput label={t("taxOffices.fields.name")} isRequired />}
					</form.AppField>
					{office ? (
						<form.AppField name="isActive">
							{(field) => <field.SwitchInput label={t("taxOffices.fields.isActive")} />}
						</form.AppField>
					) : null}
				</form>
			</Modal.Body>
			<Modal.Footer>
				<Button slot="close" variant="secondary">
					{t("common.cancel")}
				</Button>
				<form.Subscribe selector={(state) => state.isSubmitting}>
					{(isSubmitting) => (
						<Button type="submit" form={formId} isPending={isSubmitting}>
							{t("common.save")}
						</Button>
					)}
				</form.Subscribe>
			</Modal.Footer>
		</Modal.Dialog>
	);
}
