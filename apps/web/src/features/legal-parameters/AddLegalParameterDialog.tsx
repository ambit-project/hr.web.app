import { Button, Modal, toast } from "@heroui/react";
import { forwardRef, useId, useImperativeHandle, useState } from "react";
import { useTranslation } from "react-i18next";
import { z } from "zod";
import { text, useAppForm } from "#/forms";
import { applyFieldErrors } from "#/forms/serverErrors";
import { ProblemError } from "#/lib/problem";
import { useAddLegalParameter } from "./legalParametersQueries";

export type AddLegalParameterDialogHandle = { open: (code?: string) => void };

const schema = z.object({
	code: text({ required: true, max: 100 }).regex(/^\s*[a-z0-9]+(\.[a-z0-9]+)*\s*$/, "invalid"),
	value: text({ required: true, max: 30 }).regex(/^\s*-?\d+([.,]\d+)?\s*$/, "invalid"),
	validFrom: text({ required: true, max: 10 }),
	source: text({ required: true, max: 500 }),
});

const FIELDS = ["code", "value", "validFrom", "source"] as const;

export const AddLegalParameterDialog = forwardRef<AddLegalParameterDialogHandle>(
	function AddLegalParameterDialog(_props, ref) {
		const [code, setCode] = useState<string | null>(null);
		useImperativeHandle(ref, () => ({ open: (value) => setCode(value ?? "") }), []);

		return (
			<Modal>
				<Modal.Backdrop
					isDismissable={false}
					isKeyboardDismissDisabled
					isOpen={code !== null}
					onOpenChange={(open) => !open && setCode(null)}
				>
					<Modal.Container scroll="inside">
						{code !== null ? <AddForm code={code} onDone={() => setCode(null)} /> : null}
					</Modal.Container>
				</Modal.Backdrop>
			</Modal>
		);
	},
);

function AddForm({ code, onDone }: { code: string; onDone: () => void }) {
	const { t } = useTranslation();
	const formId = useId();
	const add = useAddLegalParameter();

	const form = useAppForm({
		defaultValues: { code, value: "", validFrom: "", source: "" },
		validators: { onChange: schema },
		onSubmit: async ({ value, formApi }) => {
			try {
				await add.mutateAsync({
					code: value.code.trim(),
					value: Number(value.value.trim().replace(",", ".")),
					validFrom: value.validFrom,
					source: value.source,
				});
				toast.success(t("legalParameters.add.saved", { code: value.code.trim() }));
				onDone();
			} catch (error) {
				if (
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
				<Modal.Heading>{t("legalParameters.add.title")}</Modal.Heading>
			</Modal.Header>
			<Modal.Body>
				<p className="mb-4 text-sm text-muted">{t("legalParameters.add.hint")}</p>
				<form
					id={formId}
					className="flex flex-col gap-4"
					noValidate
					onSubmit={(event) => {
						event.preventDefault();
						void form.handleSubmit();
					}}
				>
					<form.AppField name="code">
						{(field) => <field.TextInput label={t("legalParameters.fields.code")} isRequired />}
					</form.AppField>
					<div className="grid gap-4 sm:grid-cols-2">
						<form.AppField name="value">
							{(field) => <field.TextInput label={t("legalParameters.fields.value")} isRequired />}
						</form.AppField>
						<form.AppField name="validFrom">
							{(field) => (
								<field.DateInput label={t("legalParameters.fields.validFrom")} isRequired />
							)}
						</form.AppField>
					</div>
					<form.AppField name="source">
						{(field) => (
							<field.TextInput label={t("legalParameters.fields.source")} isRequired multiline />
						)}
					</form.AppField>
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
