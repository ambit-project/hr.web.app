import { Button, Modal, Spinner, toast } from "@heroui/react";
import { useQuery } from "@tanstack/react-query";
import { forwardRef, useId, useImperativeHandle, useState } from "react";
import { useTranslation } from "react-i18next";
import { z } from "zod";
import { useAppForm } from "#/forms";
import { applyFieldErrors } from "#/forms/serverErrors";
import { AMOUNT_PATTERN, amountText, formatMoney, parseAmount } from "#/lib/money";
import { onSaveError } from "./feedback";
import {
	componentTypesQuery,
	consentsQuery,
	useAddEmploymentComponent,
} from "./payComponentsQueries";
import { assignableTypes, type Calculation, signedConsentsFor } from "./types";

/** The contract a component is added to. */
export type ComponentContract = { id: string; kind: string; employeeId: string };

export type EmploymentComponentDialogHandle = { add: (contract: ComponentContract) => void };

export const EmploymentComponentDialog = forwardRef<EmploymentComponentDialogHandle>(
	function EmploymentComponentDialog(_props, ref) {
		const [contract, setContract] = useState<ComponentContract | null>(null);
		useImperativeHandle(ref, () => ({ add: setContract }), []);
		const close = () => setContract(null);

		return (
			<Modal>
				<Modal.Backdrop
					isDismissable={false}
					isKeyboardDismissDisabled
					isOpen={contract !== null}
					onOpenChange={(open) => !open && close()}
				>
					<Modal.Container scroll="inside">
						{contract ? <AddForm contract={contract} onDone={close} /> : null}
					</Modal.Container>
				</Modal.Backdrop>
			</Modal>
		);
	},
);

const schema = z.object({
	componentTypeId: z.string().min(1, "required"),
	value: z.string().trim().min(1, "required").regex(AMOUNT_PATTERN, "invalid"),
	validFrom: z.string().min(1, "required"),
	validTo: z.string(),
	consentId: z.string(),
});

const FIELDS = ["componentTypeId", "value", "validFrom", "validTo", "consentId"] as const;

function AddForm({ contract, onDone }: { contract: ComponentContract; onDone: () => void }) {
	const { t } = useTranslation();
	const formId = useId();
	const add = useAddEmploymentComponent();
	const types = useQuery(componentTypesQuery());
	const consents = useQuery(consentsQuery(contract.employeeId));
	const options = assignableTypes(types.data ?? [], contract.kind);

	const form = useAppForm({
		defaultValues: { componentTypeId: "", value: "", validFrom: "", validTo: "", consentId: "" },
		validators: { onChange: schema },
		onSubmit: async ({ value, formApi }) => {
			const type = options.find((item) => item.id === value.componentTypeId);
			try {
				await add.mutateAsync({
					employmentId: contract.id,
					request: {
						componentTypeId: value.componentTypeId,
						value: parseAmount(value.value),
						validFrom: value.validFrom,
						validTo: value.validTo === "" ? null : value.validTo,
						consentId: type?.requiresConsent && value.consentId !== "" ? value.consentId : null,
					},
				});
				toast.success(t("payComponents.components.add.added"));
				onDone();
			} catch (error) {
				onSaveError(t, error, (errors) => applyFieldErrors(formApi, errors, FIELDS), onDone);
			}
		},
	});

	return (
		<Modal.Dialog className="form-dialog">
			<Modal.Header>
				<Modal.Heading>{t("payComponents.components.add.title")}</Modal.Heading>
			</Modal.Header>
			<Modal.Body>
				{types.isPending || consents.isPending ? (
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
						<form.AppField
							name="componentTypeId"
							listeners={{ onChange: () => form.setFieldValue("consentId", "") }}
						>
							{(field) => (
								<field.SelectInput
									label={t("payComponents.components.fields.component")}
									options={options.map((type) => ({
										id: type.id,
										label: `${type.code} — ${type.name}`,
									}))}
									isRequired
								/>
							)}
						</form.AppField>
						<form.Subscribe selector={(state) => state.values.componentTypeId}>
							{(componentTypeId) => {
								const type = options.find((item) => item.id === componentTypeId);
								const signed = type ? signedConsentsFor(consents.data ?? [], type.id) : [];
								return (
									<>
										{type?.requiresConsent ? (
											signed.length === 0 ? (
												<p className="text-sm text-danger">
													{t("payComponents.components.noConsent")}
												</p>
											) : (
												<form.AppField
													name="consentId"
													listeners={{
														// The consent caps the amount, so it is the natural default.
														onChange: ({ value }) => {
															const consent = signed.find((item) => item.id === value);
															if (consent && form.getFieldValue("value") === "")
																form.setFieldValue("value", amountText(consent.monthlyAmount));
															if (consent && form.getFieldValue("validFrom") === "")
																form.setFieldValue("validFrom", consent.validFrom);
														},
													}}
												>
													{(field) => (
														<field.SelectInput
															label={t("payComponents.components.fields.consent")}
															options={signed.map((consent) => ({
																id: consent.id,
																label: consent.validTo
																	? t("payComponents.components.consentOption", {
																			amount: formatMoney(consent.monthlyAmount),
																			from: consent.validFrom,
																			to: consent.validTo,
																		})
																	: t("payComponents.components.consentOptionOpen", {
																			amount: formatMoney(consent.monthlyAmount),
																			from: consent.validFrom,
																		}),
															}))}
															isRequired
														/>
													)}
												</form.AppField>
											)
										) : null}
										<div className="grid gap-4 sm:grid-cols-2">
											<form.AppField name="value">
												{(field) => (
													<field.TextInput
														label={t(
															`payComponents.components.valueLabels.${(type?.calculation ?? "fixed") as Calculation}`,
														)}
														isRequired
													/>
												)}
											</form.AppField>
										</div>
									</>
								);
							}}
						</form.Subscribe>
						<div className="grid gap-4 sm:grid-cols-2">
							<form.AppField name="validFrom">
								{(field) => (
									<field.DateInput
										label={t("payComponents.components.fields.validFrom")}
										isRequired
									/>
								)}
							</form.AppField>
							<form.AppField name="validTo">
								{(field) => (
									<field.DateInput label={t("payComponents.components.fields.validTo")} />
								)}
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
