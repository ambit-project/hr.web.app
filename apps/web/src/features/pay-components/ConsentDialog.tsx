import { Button, Modal, Spinner, toast } from "@heroui/react";
import { useQuery } from "@tanstack/react-query";
import { forwardRef, useId, useImperativeHandle, useState } from "react";
import { useTranslation } from "react-i18next";
import { z } from "zod";
import type { ListConsentsItem } from "#/api/models";
import { useAppForm } from "#/forms";
import { onSaveError } from "#/forms/feedback";
import { applyFieldErrors } from "#/forms/serverErrors";
import { AMOUNT_PATTERN, amountText, parseAmount } from "#/lib/money";
import { componentTypesQuery, useCreateConsent, useUpdateConsent } from "./payComponentsQueries";

export type ConsentDialogHandle = {
	create: (employeeId: string) => void;
	/** Only an unsigned consent can be changed; setting the signing date signs it. */
	edit: (item: ListConsentsItem) => void;
};

type Target = { mode: "create"; employeeId: string } | { mode: "edit"; item: ListConsentsItem };

export const ConsentDialog = forwardRef<ConsentDialogHandle>(function ConsentDialog(_props, ref) {
	const [target, setTarget] = useState<Target | null>(null);
	useImperativeHandle(
		ref,
		() => ({
			create: (employeeId) => setTarget({ mode: "create", employeeId }),
			edit: (item) => setTarget({ mode: "edit", item }),
		}),
		[],
	);
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
					{target ? <ConsentForm target={target} onDone={close} /> : null}
				</Modal.Container>
			</Modal.Backdrop>
		</Modal>
	);
});

const schema = z.object({
	componentTypeId: z.string().min(1, "required"),
	monthlyAmount: z.string().trim().min(1, "required").regex(AMOUNT_PATTERN, "invalid"),
	validFrom: z.string().min(1, "required"),
	validTo: z.string(),
	signedAt: z.string(),
});

const FIELDS = ["componentTypeId", "monthlyAmount", "validFrom", "validTo", "signedAt"] as const;

function ConsentForm({ target, onDone }: { target: Target; onDone: () => void }) {
	const { t } = useTranslation();
	const formId = useId();
	const create = useCreateConsent();
	const update = useUpdateConsent();
	const types = useQuery(componentTypesQuery());
	const item = target.mode === "edit" ? target.item : null;
	const options = (types.data ?? []).filter((type) => type.isActive && type.requiresConsent);

	const form = useAppForm({
		defaultValues: {
			componentTypeId: item?.componentTypeId ?? "",
			monthlyAmount: amountText(item?.monthlyAmount),
			validFrom: item?.validFrom ?? "",
			validTo: item?.validTo ?? "",
			signedAt: item?.signedAt ?? "",
		},
		validators: { onChange: schema },
		onSubmit: async ({ value, formApi }) => {
			const fields = {
				monthlyAmount: parseAmount(value.monthlyAmount),
				validFrom: value.validFrom,
				validTo: value.validTo === "" ? null : value.validTo,
				signedAt: value.signedAt === "" ? null : value.signedAt,
			};
			try {
				if (item) {
					await update.mutateAsync({
						id: item.id,
						request: { rowVersion: item.rowVersion, ...fields },
					});
					toast.success(t("payComponents.consents.edit.saved"));
				} else if (target.mode === "create") {
					await create.mutateAsync({
						employeeId: target.employeeId,
						request: { componentTypeId: value.componentTypeId, ...fields },
					});
					toast.success(t("payComponents.consents.create.created"));
				}
				onDone();
			} catch (error) {
				onSaveError(t, error, (errors) => applyFieldErrors(formApi, errors, FIELDS), onDone);
			}
		},
	});

	return (
		<Modal.Dialog className="form-dialog">
			<Modal.Header>
				<Modal.Heading>
					{item
						? t("payComponents.consents.edit.title", { name: item.name })
						: t("payComponents.consents.create.title")}
				</Modal.Heading>
			</Modal.Header>
			<Modal.Body>
				{types.isPending ? (
					<Spinner aria-label={t("common.loading")} />
				) : (
					<form
						id={formId}
						className="flex flex-col gap-4"
						noValidate
						onSubmit={(event) => {
							event.preventDefault();
							void form.handleSubmit();
						}}
					>
						{item ? null : options.length === 0 ? (
							<p className="text-sm text-danger">{t("payComponents.consents.noTypes")}</p>
						) : (
							<form.AppField name="componentTypeId">
								{(field) => (
									<field.SelectInput
										label={t("payComponents.consents.fields.component")}
										options={options.map((type) => ({
											id: type.id,
											label: `${type.code} — ${type.name}`,
										}))}
										isRequired
									/>
								)}
							</form.AppField>
						)}
						<div className="grid gap-4 sm:grid-cols-2">
							<form.AppField name="monthlyAmount">
								{(field) => (
									<field.TextInput
										label={t("payComponents.consents.fields.monthlyAmount")}
										isRequired
									/>
								)}
							</form.AppField>
							<div className="flex flex-col gap-1">
								<form.AppField name="signedAt">
									{(field) => (
										<field.DateInput label={t("payComponents.consents.fields.signedAt")} />
									)}
								</form.AppField>
								<p className="text-xs text-muted">{t("payComponents.consents.signedAtHint")}</p>
							</div>
						</div>
						<div className="grid gap-4 sm:grid-cols-2">
							<form.AppField name="validFrom">
								{(field) => (
									<field.DateInput
										label={t("payComponents.consents.fields.validFrom")}
										isRequired
									/>
								)}
							</form.AppField>
							<form.AppField name="validTo">
								{(field) => <field.DateInput label={t("payComponents.consents.fields.validTo")} />}
							</form.AppField>
						</div>
					</form>
				)}
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
