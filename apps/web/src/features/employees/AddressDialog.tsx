import { Button, Label, ListBox, Modal, Select, toast } from "@heroui/react";
import { forwardRef, useId, useImperativeHandle, useState } from "react";
import { useTranslation } from "react-i18next";
import { z } from "zod";
import { text, useAppForm } from "#/forms";
import { applyFieldErrors } from "#/forms/serverErrors";
import { ProblemError } from "#/lib/problem";
import { useAddEmployeeAddress } from "./employeesQueries";
import { ADDRESS_KINDS, type AddressKind } from "./types";

export type AddressDialogHandle = { open: (kind: AddressKind) => void };

const schema = z.object({
	kind: z.enum(ADDRESS_KINDS),
	street: text({ required: true, max: 200 }),
	postalCode: text({ required: true, max: 6 }).regex(/^\s*\d{2}-\d{3}\s*$/, "invalid"),
	city: text({ required: true, max: 100 }),
	validFrom: text({ required: true, max: 10 }),
});

const FIELDS = ["kind", "street", "postalCode", "city", "validFrom"] as const;

export const AddressDialog = forwardRef<AddressDialogHandle, { employeeId: string }>(
	function AddressDialog({ employeeId }, ref) {
		const [kind, setKind] = useState<AddressKind | null>(null);
		useImperativeHandle(ref, () => ({ open: setKind }), []);

		return (
			<Modal>
				<Modal.Backdrop isOpen={kind !== null} onOpenChange={(open) => !open && setKind(null)}>
					<Modal.Container scroll="inside">
						{kind ? (
							<AddressForm employeeId={employeeId} kind={kind} onDone={() => setKind(null)} />
						) : null}
					</Modal.Container>
				</Modal.Backdrop>
			</Modal>
		);
	},
);

function AddressForm({
	employeeId,
	kind,
	onDone,
}: {
	employeeId: string;
	kind: AddressKind;
	onDone: () => void;
}) {
	const { t } = useTranslation();
	const formId = useId();
	const add = useAddEmployeeAddress();

	const form = useAppForm({
		defaultValues: { kind, street: "", postalCode: "", city: "", validFrom: "" },
		validators: { onChange: schema },
		onSubmit: async ({ value, formApi }) => {
			try {
				await add.mutateAsync({ id: employeeId, request: value });
				toast.success(t("employees.addresses.saved"));
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
				<Modal.Heading>{t("employees.addresses.add")}</Modal.Heading>
			</Modal.Header>
			<Modal.Body>
				<p className="mb-4 text-sm text-muted">{t("employees.addresses.hint")}</p>
				<form
					id={formId}
					className="flex flex-col gap-4"
					noValidate
					onSubmit={(event) => {
						event.preventDefault();
						void form.handleSubmit();
					}}
				>
					<form.Field name="kind">
						{(field) => (
							<Select
								value={field.state.value}
								onChange={(key) => key !== null && field.handleChange(key as AddressKind)}
							>
								<Label>{t("employees.addresses.kind")}</Label>
								<Select.Trigger>
									<Select.Value />
									<Select.Indicator />
								</Select.Trigger>
								<Select.Popover>
									<ListBox>
										{ADDRESS_KINDS.map((item) => (
											<ListBox.Item
												key={item}
												id={item}
												textValue={t(`employees.addresses.${item}`)}
											>
												{t(`employees.addresses.${item}`)}
												<ListBox.ItemIndicator />
											</ListBox.Item>
										))}
									</ListBox>
								</Select.Popover>
							</Select>
						)}
					</form.Field>
					<form.AppField name="street">
						{(field) => <field.TextInput label={t("employees.fields.street")} isRequired />}
					</form.AppField>
					<div className="grid gap-4 sm:grid-cols-[12rem_1fr]">
						<form.AppField name="postalCode">
							{(field) => <field.TextInput label={t("employees.fields.postalCode")} isRequired />}
						</form.AppField>
						<form.AppField name="city">
							{(field) => <field.TextInput label={t("employees.fields.city")} isRequired />}
						</form.AppField>
					</div>
					<form.AppField name="validFrom">
						{(field) => <field.DateInput label={t("employees.fields.validFrom")} isRequired />}
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
