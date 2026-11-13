import { Button, Modal, toast } from "@heroui/react";
import { forwardRef, useId, useImperativeHandle, useState } from "react";
import { useTranslation } from "react-i18next";
import { z } from "zod";
import type { GetPayrollResponse } from "#/api/models";
import { useAppForm } from "#/forms";
import { onSaveError } from "#/forms/feedback";
import { applyFieldErrors } from "#/forms/serverErrors";
import { useSetPayrollPpkPaid } from "./payrollQueries";

export type PpkPaidDialogHandle = { open: (payroll: GetPayrollResponse) => void };

/**
 * Date the list's PPK contributions reached the financial institution (009 R19): from that day the employer part is
 * the employee's income, taxed by a list paid later in the same month or reported on PIT-11.
 */
export const PpkPaidDialog = forwardRef<PpkPaidDialogHandle>(function PpkPaidDialog(_props, ref) {
	const [payroll, setPayroll] = useState<GetPayrollResponse | null>(null);
	useImperativeHandle(ref, () => ({ open: setPayroll }), []);
	const close = () => setPayroll(null);

	return (
		<Modal>
			<Modal.Backdrop
				isDismissable={false}
				isKeyboardDismissDisabled
				isOpen={payroll !== null}
				onOpenChange={(open) => !open && close()}
			>
				<Modal.Container scroll="inside">
					{payroll ? <PpkPaidForm payroll={payroll} onDone={close} /> : null}
				</Modal.Container>
			</Modal.Backdrop>
		</Modal>
	);
});

const schema = z.object({ paidOn: z.string() });

function PpkPaidForm({ payroll, onDone }: { payroll: GetPayrollResponse; onDone: () => void }) {
	const { t } = useTranslation();
	const formId = useId();
	const save = useSetPayrollPpkPaid();

	const form = useAppForm({
		defaultValues: { paidOn: payroll.ppkPaidOn ?? "" },
		validators: { onChange: schema },
		onSubmit: async ({ value, formApi }) => {
			try {
				await save.mutateAsync({
					id: payroll.id,
					request: { rowVersion: payroll.rowVersion, paidOn: value.paidOn || null },
				});
				toast.success(t("payroll.page.ppkPaid.saved"));
				onDone();
			} catch (error) {
				onSaveError(t, error, (errors) => applyFieldErrors(formApi, errors, ["paidOn"]), onDone);
			}
		},
	});

	return (
		<Modal.Dialog className="form-dialog">
			<Modal.Header>
				<Modal.Heading>{t("payroll.page.ppkPaid.title", { number: payroll.number })}</Modal.Heading>
			</Modal.Header>
			<Modal.Body>
				<p className="mb-4 text-sm text-muted">{t("payroll.page.ppkPaid.hint")}</p>
				<form
					id={formId}
					className="grid gap-4 sm:grid-cols-2"
					noValidate
					onSubmit={(event) => {
						event.preventDefault();
						void form.handleSubmit();
					}}
				>
					<form.AppField name="paidOn">
						{(field) => <field.DateInput label={t("payroll.page.ppkPaid.paidOn")} />}
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
