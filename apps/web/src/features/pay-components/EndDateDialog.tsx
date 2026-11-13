import { Button, Modal, toast } from "@heroui/react";
import { forwardRef, useId, useImperativeHandle, useState } from "react";
import { useTranslation } from "react-i18next";
import { z } from "zod";
import { useAppForm } from "#/forms";
import { onSaveError } from "#/forms/feedback";
import { applyFieldErrors } from "#/forms/serverErrors";

/** What to end: the dialog asks for the last day; an empty day undoes the end. */
export type EndTarget = {
	title: string;
	hint: string;
	validTo: string | null;
	save: (validTo: string | null) => Promise<unknown>;
	savedMessage: string;
	undoneMessage: string;
};

export type EndDateDialogHandle = { open: (target: EndTarget) => void };

/** End of a contract component or a consent: one date, empty = undo (both routines take a nullable validTo). */
export const EndDateDialog = forwardRef<EndDateDialogHandle>(function EndDateDialog(_props, ref) {
	const [target, setTarget] = useState<EndTarget | null>(null);
	useImperativeHandle(ref, () => ({ open: setTarget }), []);
	const close = () => setTarget(null);

	return (
		<Modal>
			<Modal.Backdrop
				isDismissable={false}
				isKeyboardDismissDisabled
				isOpen={target !== null}
				onOpenChange={(open) => !open && close()}
			>
				<Modal.Container scroll="inside">
					{target ? <EndForm target={target} onDone={close} /> : null}
				</Modal.Container>
			</Modal.Backdrop>
		</Modal>
	);
});

const schema = z.object({ validTo: z.string() });

function EndForm({ target, onDone }: { target: EndTarget; onDone: () => void }) {
	const { t } = useTranslation();
	const formId = useId();

	const form = useAppForm({
		defaultValues: { validTo: target.validTo ?? "" },
		// Shape only; it also lets a later edit clear the server's field errors (see applyFieldErrors).
		validators: { onChange: schema },
		onSubmit: async ({ value, formApi }) => {
			const validTo = value.validTo === "" ? null : value.validTo;
			try {
				await target.save(validTo);
				toast.success(validTo ? target.savedMessage : target.undoneMessage);
				onDone();
			} catch (error) {
				onSaveError(t, error, (errors) => applyFieldErrors(formApi, errors, ["validTo"]), onDone);
			}
		},
	});

	return (
		<Modal.Dialog className="form-dialog">
			<Modal.Header>
				<Modal.Heading>{target.title}</Modal.Heading>
			</Modal.Header>
			<Modal.Body>
				<p className="mb-4 text-sm text-muted">{target.hint}</p>
				<form
					id={formId}
					className="grid gap-4 sm:grid-cols-2"
					noValidate
					onSubmit={(event) => {
						event.preventDefault();
						void form.handleSubmit();
					}}
				>
					<form.AppField name="validTo">
						{(field) => <field.DateInput label={t("payComponents.endDate")} />}
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
