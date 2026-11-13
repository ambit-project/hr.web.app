import { Button, Modal, toast } from "@heroui/react";
import { forwardRef, useId, useImperativeHandle, useState } from "react";
import { useTranslation } from "react-i18next";
import { z } from "zod";
import type { ListComponentTypesItem } from "#/api/models";
import { text, useAppForm } from "#/forms";
import { applyFieldErrors } from "#/forms/serverErrors";
import { onSaveError } from "./feedback";
import { useCreateComponentType, useUpdateComponentType } from "./payComponentsQueries";
import {
	ADDITION_FLAGS,
	type Calculation,
	COMPONENT_KINDS,
	type ComponentKind,
	calculationsFor,
	SELECTABLE_CATEGORIES,
} from "./types";

export type PayComponentTypeDialogHandle = {
	create: () => void;
	edit: (item: ListComponentTypesItem) => void;
};

type Target = { mode: "create" } | { mode: "edit"; item: ListComponentTypesItem };

export const PayComponentTypeDialog = forwardRef<PayComponentTypeDialogHandle>(
	function PayComponentTypeDialog(_props, ref) {
		const [target, setTarget] = useState<Target | null>(null);
		useImperativeHandle(
			ref,
			() => ({
				create: () => setTarget({ mode: "create" }),
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
						{target ? <TypeForm target={target} onDone={close} /> : null}
					</Modal.Container>
				</Modal.Backdrop>
			</Modal>
		);
	},
);

// Mirrors component_types_code; lower case is accepted because the database upper-cases the code.
const CODE_PATTERN = /^\s*[A-Za-z0-9-]{1,20}\s*$/;

const schema = z.object({
	code: z.string().trim().min(1, "required").regex(CODE_PATTERN, "invalid"),
	name: text({ required: true, max: 200 }),
	kind: z.enum(COMPONENT_KINDS),
	calculation: z.string().min(1, "required"),
	inZusBase: z.boolean(),
	inHealthBase: z.boolean(),
	taxable: z.boolean(),
	inSicknessBase: z.boolean(),
	deductionCategory: z.string(),
	isActive: z.boolean(),
});

const FIELDS = [
	"code",
	"name",
	"kind",
	"calculation",
	"inZusBase",
	"deductionCategory",
	"isActive",
] as const;

function TypeForm({ target, onDone }: { target: Target; onDone: () => void }) {
	const { t } = useTranslation();
	const formId = useId();
	const create = useCreateComponentType();
	const update = useUpdateComponentType();
	const item = target.mode === "edit" ? target.item : null;

	const form = useAppForm({
		defaultValues: {
			code: item?.code ?? "",
			name: item?.name ?? "",
			kind: (item?.kind ?? "addition") as ComponentKind,
			calculation: item?.calculation ?? "fixed",
			inZusBase: item?.inZusBase ?? true,
			inHealthBase: item?.inHealthBase ?? true,
			taxable: item?.taxable ?? true,
			inSicknessBase: item?.inSicknessBase ?? false,
			deductionCategory: item?.deductionCategory ?? "",
			isActive: item?.isActive ?? true,
		},
		validators: { onChange: schema },
		onSubmit: async ({ value, formApi }) => {
			const addition = value.kind === "addition";
			// Fields the kind does not have are sent empty, as component_type_validate expects.
			const features = {
				inZusBase: addition ? value.inZusBase : null,
				inHealthBase: addition ? value.inHealthBase : null,
				taxable: addition ? value.taxable : null,
				inSicknessBase: addition ? value.inSicknessBase : null,
				deductionCategory:
					addition || value.deductionCategory === "" ? null : value.deductionCategory,
			};
			try {
				if (item) {
					await update.mutateAsync({
						id: item.id,
						request: {
							rowVersion: item.rowVersion,
							name: value.name.trim(),
							calculation: value.calculation,
							isActive: value.isActive,
							...features,
						},
					});
					toast.success(t("payComponents.edit.saved", { code: item.code }));
				} else {
					const code = value.code.trim().toUpperCase();
					await create.mutateAsync({
						code,
						name: value.name.trim(),
						kind: value.kind,
						calculation: value.calculation,
						...features,
					});
					toast.success(t("payComponents.create.created", { code }));
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
						? t("payComponents.edit.title", { code: item.code })
						: t("payComponents.create.title")}
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
					{item ? (
						<dl className="grid grid-cols-[10rem_1fr] gap-x-4 gap-y-1 text-sm">
							<dt className="text-muted">{t("payComponents.fields.code")}</dt>
							<dd>
								<code>{item.code}</code>
							</dd>
							<dt className="text-muted">{t("payComponents.fields.kind")}</dt>
							<dd>{t(`payComponents.kinds.${item.kind as ComponentKind}`)}</dd>
						</dl>
					) : (
						<div className="grid gap-4 sm:grid-cols-2">
							<form.AppField name="code">
								{(field) => (
									<field.TextInput
										label={t("payComponents.fields.code")}
										description={t("payComponents.codeHint")}
										isRequired
									/>
								)}
							</form.AppField>
							<form.AppField
								name="kind"
								listeners={{
									onChange: ({ value }) => {
										if (
											!calculationsFor(value).includes(
												form.getFieldValue("calculation") as Calculation,
											)
										)
											form.setFieldValue("calculation", "fixed");
									},
								}}
							>
								{(field) => (
									<field.SelectInput
										label={t("payComponents.fields.kind")}
										options={COMPONENT_KINDS.map((kind) => ({
											id: kind,
											label: t(`payComponents.kinds.${kind}`),
										}))}
										isRequired
									/>
								)}
							</form.AppField>
						</div>
					)}
					<form.AppField name="name">
						{(field) => <field.TextInput label={t("payComponents.fields.name")} isRequired />}
					</form.AppField>
					<form.Subscribe selector={(state) => state.values.kind}>
						{(kind) => (
							<>
								<div className="grid gap-4 sm:grid-cols-2">
									<form.AppField name="calculation">
										{(field) => (
											<field.SelectInput
												label={t("payComponents.fields.calculation")}
												options={calculationsFor(kind).map((calculation) => ({
													id: calculation,
													label: t(`payComponents.calculations.${calculation}`),
												}))}
												isDisabled={item?.isUsed}
												isRequired
											/>
										)}
									</form.AppField>
									{kind === "deduction" ? (
										<form.AppField name="deductionCategory">
											{(field) => (
												<field.SelectInput
													label={t("payComponents.fields.deductionCategory")}
													options={SELECTABLE_CATEGORIES.map((category) => ({
														id: category,
														label: t(`payComponents.categories.${category}`),
													}))}
													isRequired
												/>
											)}
										</form.AppField>
									) : null}
								</div>
								{item?.isUsed ? (
									<p className="text-sm text-muted">{t("payComponents.usedHint")}</p>
								) : null}
								{kind === "addition" ? (
									<fieldset className="flex flex-col gap-2">
										<legend className="mb-2 text-sm font-medium">
											{t("payComponents.fields.features")}
										</legend>
										<div className="grid gap-2 sm:grid-cols-2">
											{ADDITION_FLAGS.map((flag) => (
												<form.AppField key={flag} name={flag}>
													{(field) => (
														<field.CheckboxInput label={t(`payComponents.flags.${flag}`)} />
													)}
												</form.AppField>
											))}
										</div>
										<p className="text-sm text-muted">{t("payComponents.flagsHint")}</p>
									</fieldset>
								) : null}
							</>
						)}
					</form.Subscribe>
					{item ? (
						<form.AppField name="isActive">
							{(field) => <field.SwitchInput label={t("payComponents.fields.isActive")} />}
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
