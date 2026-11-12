import { Button, Drawer, Label, ListBox, Select, toast } from "@heroui/react";
import { forwardRef, useId, useImperativeHandle, useState } from "react";
import { useTranslation } from "react-i18next";
import { z } from "zod";
import { text, useAppForm } from "#/forms";
import { applyFieldErrors } from "#/forms/serverErrors";
import { ProblemError } from "#/lib/problem";
import { useAddEmployeeAddress } from "./employeesQueries";
import { ADDRESS_KINDS, type AddressKind } from "./types";

export type AddressDrawerHandle = { open: (kind: AddressKind) => void };

const schema = z.object({
	kind: z.enum(ADDRESS_KINDS),
	street: text({ required: true, max: 200 }),
	postalCode: text({ required: true, max: 6 }).regex(/^\s*\d{2}-\d{3}\s*$/, "invalid"),
	city: text({ required: true, max: 100 }),
	validFrom: text({ required: true, max: 10 }),
});

const FIELDS = ["kind", "street", "postalCode", "city", "validFrom"] as const;

export const AddressDrawer = forwardRef<AddressDrawerHandle, { employeeId: string }>(
	function AddressDrawer({ employeeId }, ref) {
		const [kind, setKind] = useState<AddressKind | null>(null);
		useImperativeHandle(ref, () => ({ open: setKind }), []);

		return (
			<Drawer>
				<Drawer.Backdrop isOpen={kind !== null} onOpenChange={(open) => !open && setKind(null)}>
					<Drawer.Content placement="right">
						{kind ? (
							<AddressForm employeeId={employeeId} kind={kind} onDone={() => setKind(null)} />
						) : null}
					</Drawer.Content>
				</Drawer.Backdrop>
			</Drawer>
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
		<Drawer.Dialog>
			<Drawer.Header>
				<Drawer.Heading>{t("employees.addresses.add")}</Drawer.Heading>
			</Drawer.Header>
			<Drawer.Body>
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
					<form.AppField name="postalCode">
						{(field) => <field.TextInput label={t("employees.fields.postalCode")} isRequired />}
					</form.AppField>
					<form.AppField name="city">
						{(field) => <field.TextInput label={t("employees.fields.city")} isRequired />}
					</form.AppField>
					<form.AppField name="validFrom">
						{(field) => <field.DateInput label={t("employees.fields.validFrom")} isRequired />}
					</form.AppField>
				</form>
			</Drawer.Body>
			<Drawer.Footer>
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
			</Drawer.Footer>
		</Drawer.Dialog>
	);
}
