import { Button, Modal, toast } from "@heroui/react";
import { forwardRef, useId, useImperativeHandle, useState } from "react";
import { useTranslation } from "react-i18next";
import { z } from "zod";
import type { ListAbsencesItem, ListAbsenceTypesItem } from "#/api/models";
import { text, useAppForm } from "#/forms";
import { onSaveError } from "#/forms/feedback";
import { applyFieldErrors } from "#/forms/serverErrors";
import { Notice } from "./Notice";
import { useCreateAbsence, useUpdateAbsence } from "./timeAbsenceQueries";
import { formatHours, formatNumber, hoursToDays, remainingFrom } from "./types";

export type AbsenceDialogHandle = {
	create: (employmentId: string, types: ListAbsenceTypesItem[]) => void;
	edit: (item: ListAbsencesItem, types: ListAbsenceTypesItem[]) => void;
};

type Target =
	| { mode: "create"; employmentId: string; types: ListAbsenceTypesItem[] }
	| { mode: "edit"; item: ListAbsencesItem; types: ListAbsenceTypesItem[] };

export const AbsenceDialog = forwardRef<AbsenceDialogHandle>(function AbsenceDialog(_props, ref) {
	const [target, setTarget] = useState<Target | null>(null);
	useImperativeHandle(
		ref,
		() => ({
			create: (employmentId, types) => setTarget({ mode: "create", employmentId, types }),
			edit: (item, types) => setTarget({ mode: "edit", item, types }),
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
					{target ? <AbsenceForm target={target} onDone={close} /> : null}
				</Modal.Container>
			</Modal.Backdrop>
		</Modal>
	);
});

const FIELDS = ["absenceTypeId", "dateFrom", "dateTo", "certificateNumber", "note"] as const;

function AbsenceForm({ target, onDone }: { target: Target; onDone: () => void }) {
	const { t } = useTranslation();
	const formId = useId();
	const create = useCreateAbsence();
	const update = useUpdateAbsence();
	const item = target.mode === "edit" ? target.item : null;
	// What the database said is left when a limit was hit; cleared by the next try.
	const [remaining, setRemaining] = useState<ReturnType<typeof remainingFrom>>(null);
	const isSickness = (code: string) =>
		target.types.find((type) => type.code === code)?.sickness ?? false;

	// The e-ZLA number belongs to sickness only (R7): required there, absent elsewhere.
	const schema = z
		.object({
			absenceTypeId: z.string().min(1, "required"),
			dateFrom: z.string().min(1, "required"),
			dateTo: z.string().min(1, "required"),
			certificateNumber: z.string().max(20, "maxLength"),
			note: text({ max: 2000 }),
		})
		.superRefine((value, ctx) => {
			if (value.dateFrom && value.dateTo && value.dateTo < value.dateFrom) {
				ctx.addIssue({ code: "custom", path: ["dateTo"], message: "beforeFrom" });
			}
			if (isSickness(value.absenceTypeId) && value.certificateNumber.trim() === "") {
				ctx.addIssue({ code: "custom", path: ["certificateNumber"], message: "required" });
			}
		});

	const form = useAppForm({
		defaultValues: {
			absenceTypeId: item?.absenceTypeId ?? "",
			dateFrom: item?.dateFrom ?? "",
			dateTo: item?.dateTo ?? "",
			certificateNumber: item?.certificateNumber ?? "",
			note: item?.note ?? "",
		},
		validators: { onChange: schema },
		onSubmit: async ({ value, formApi }) => {
			setRemaining(null);
			const request = {
				absenceTypeId: value.absenceTypeId,
				dateFrom: value.dateFrom,
				dateTo: value.dateTo,
				certificateNumber: isSickness(value.absenceTypeId) ? value.certificateNumber.trim() : null,
				note: value.note.trim() === "" ? null : value.note.trim(),
			};
			try {
				if (item) {
					await update.mutateAsync({
						id: item.id,
						request: { rowVersion: item.rowVersion, ...request },
					});
					toast.success(t("timeAbsence.absences.edit.saved"));
				} else if (target.mode === "create") {
					await create.mutateAsync({ employmentId: target.employmentId, request });
					toast.success(t("timeAbsence.absences.create.created"));
				}
				onDone();
			} catch (error) {
				onSaveError(
					t,
					error,
					(errors) => {
						setRemaining(remainingFrom(errors));
						return applyFieldErrors(formApi, errors, FIELDS);
					},
					onDone,
				);
			}
		},
	});

	const limitText = (type: ListAbsenceTypesItem) => {
		const kind = type.limitKind as "none" | "leave" | "yearDays" | "event";
		const days = type.limitDays ?? 0;
		const base = t(`timeAbsence.absences.limitKinds.${kind}`, { days });
		// UZ: leave limit and at most 4 days a year.
		return kind === "leave" && type.limitDays
			? `${base}, ${t("timeAbsence.absences.limitKinds.yearDays", { days })}`
			: base;
	};

	return (
		<Modal.Dialog className="form-dialog">
			<Modal.Header>
				<Modal.Heading>
					{item
						? t("timeAbsence.absences.edit.title", { type: item.absenceTypeName })
						: t("timeAbsence.absences.create.title")}
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
					<form.AppField name="absenceTypeId">
						{(field) => (
							<field.SelectInput
								label={t("timeAbsence.absences.fields.type")}
								options={target.types.map((type) => ({
									id: type.code,
									label: `${type.code} — ${type.name}`,
								}))}
								isRequired
							/>
						)}
					</form.AppField>
					<form.Subscribe selector={(state) => state.values.absenceTypeId}>
						{(code) => {
							const type = target.types.find((candidate) => candidate.code === code);
							return type ? (
								<p className="-mt-2 text-sm text-muted">
									{t("timeAbsence.absences.typeHint", { limit: limitText(type) })}
									{type.legalBasis ? ` · ${type.legalBasis}` : null}
								</p>
							) : null;
						}}
					</form.Subscribe>
					<div className="grid gap-4 sm:grid-cols-2">
						<form.AppField name="dateFrom">
							{(field) => (
								<field.DateInput label={t("timeAbsence.absences.fields.dateFrom")} isRequired />
							)}
						</form.AppField>
						<form.AppField name="dateTo">
							{(field) => (
								<field.DateInput label={t("timeAbsence.absences.fields.dateTo")} isRequired />
							)}
						</form.AppField>
					</div>
					{remaining ? (
						<Notice status="danger">
							{remaining.unit === "days"
								? t("timeAbsence.absences.remainingDays", {
										days: formatNumber(remaining.value),
									})
								: t("timeAbsence.absences.remainingHours", {
										hours: formatHours(remaining.value),
										days: formatNumber(hoursToDays(remaining.value)),
									})}
						</Notice>
					) : null}
					<form.Subscribe selector={(state) => state.values.absenceTypeId}>
						{(code) =>
							isSickness(code) ? (
								<div className="grid gap-4 sm:grid-cols-2">
									<form.AppField name="certificateNumber">
										{(field) => (
											<field.TextInput
												label={t("timeAbsence.absences.fields.certificateNumber")}
												description={t("timeAbsence.absences.certificateHint")}
												isRequired
											/>
										)}
									</form.AppField>
								</div>
							) : null
						}
					</form.Subscribe>
					<form.AppField name="note">
						{(field) => <field.TextInput label={t("timeAbsence.absences.fields.note")} multiline />}
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
