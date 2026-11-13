import { Button, Modal, toast } from "@heroui/react";
import { forwardRef, useId, useImperativeHandle, useState } from "react";
import { useTranslation } from "react-i18next";
import { z } from "zod";
import { text, useAppForm } from "#/forms";
import { applyFieldErrors } from "#/forms/serverErrors";
import { ProblemError } from "#/lib/problem";
import { useAddEmployeeBankAccount, useCloseEmployeeBankAccount } from "./employeesQueries";

type Mode = "add" | "close";
export type BankAccountDialogHandle = { open: (mode: Mode) => void };

const addSchema = z.object({
	iban: text({ required: true, max: 40 }),
	date: text({ required: true, max: 10 }),
});

export const BankAccountDialog = forwardRef<BankAccountDialogHandle, { employeeId: string }>(
	function BankAccountDialog({ employeeId }, ref) {
		const [mode, setMode] = useState<Mode | null>(null);
		useImperativeHandle(ref, () => ({ open: setMode }), []);

		return (
			<Modal>
				<Modal.Backdrop isOpen={mode !== null} onOpenChange={(open) => !open && setMode(null)}>
					<Modal.Container scroll="inside">
						{mode ? (
							<BankAccountForm employeeId={employeeId} mode={mode} onDone={() => setMode(null)} />
						) : null}
					</Modal.Container>
				</Modal.Backdrop>
			</Modal>
		);
	},
);

function BankAccountForm({
	employeeId,
	mode,
	onDone,
}: {
	employeeId: string;
	mode: Mode;
	onDone: () => void;
}) {
	const { t } = useTranslation();
	const formId = useId();
	const add = useAddEmployeeBankAccount();
	const close = useCloseEmployeeBankAccount();

	const form = useAppForm({
		defaultValues: { iban: mode === "close" ? "-" : "", date: "" },
		validators: { onChange: addSchema },
		onSubmit: async ({ value, formApi }) => {
			try {
				if (mode === "add") {
					await add.mutateAsync({
						id: employeeId,
						request: { iban: value.iban, validFrom: value.date },
					});
				} else {
					await close.mutateAsync({ id: employeeId, request: { validTo: value.date } });
				}
				toast.success(t("employees.bank.saved"));
				onDone();
			} catch (error) {
				if (error instanceof ProblemError && error.problem.status === 409) {
					toast.warning(t("common.conflict"));
					onDone();
					return;
				}
				// The API names the date validFrom (add) or validTo (close); the form has one date field.
				const errors = error instanceof ProblemError ? error.problem.errors : undefined;
				const mapped = errors
					? {
							iban: errors.iban ?? [],
							date: errors.validFrom ?? errors.validTo ?? [],
						}
					: undefined;
				const known = Object.fromEntries(
					Object.entries(mapped ?? {}).filter(([, codes]) => codes.length > 0),
				);
				if (!applyFieldErrors(formApi, known, ["iban", "date"] as const)) {
					toast.danger(t("common.serverError"));
				}
			}
		},
	});

	return (
		<Modal.Dialog className="form-dialog">
			<Modal.Header>
				<Modal.Heading>
					{mode === "add" ? t("employees.bank.add") : t("employees.bank.close")}
				</Modal.Heading>
			</Modal.Header>
			<Modal.Body>
				<p className="mb-4 text-sm text-muted">
					{mode === "add" ? t("employees.bank.addHint") : t("employees.bank.closeHint")}
				</p>
				<form
					id={formId}
					className="flex flex-col gap-4"
					noValidate
					onSubmit={(event) => {
						event.preventDefault();
						void form.handleSubmit();
					}}
				>
					{mode === "add" ? (
						<form.AppField name="iban">
							{(field) => <field.TextInput label={t("employees.fields.iban")} isRequired />}
						</form.AppField>
					) : null}
					<form.AppField name="date">
						{(field) => (
							<field.DateInput
								label={
									mode === "add" ? t("employees.fields.validFrom") : t("employees.fields.validTo")
								}
								isRequired
							/>
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
