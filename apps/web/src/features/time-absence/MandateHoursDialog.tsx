import { Button, Modal, toast } from "@heroui/react";
import { forwardRef, useId, useImperativeHandle, useState } from "react";
import { useTranslation } from "react-i18next";
import { z } from "zod";
import type { ListMandateHoursItem } from "#/api/models";
import { useAppForm } from "#/forms";
import { onSaveError } from "#/forms/feedback";
import { applyFieldErrors } from "#/forms/serverErrors";
import { useAddMandateHours, useUpdateMandateHours } from "./timeAbsenceQueries";
import { CONFIRMATION_FORMS, HOURS_PATTERN, hoursText, parseHours } from "./types";

export type MandateHoursDialogHandle = {
	create: (employmentId: string) => void;
	edit: (item: ListMandateHoursItem) => void;
};

type Target =
	| { mode: "create"; employmentId: string }
	| { mode: "edit"; item: ListMandateHoursItem };

export const MandateHoursDialog = forwardRef<MandateHoursDialogHandle>(
	function MandateHoursDialog(_props, ref) {
		const [target, setTarget] = useState<Target | null>(null);
		useImperativeHandle(
			ref,
			() => ({
				create: (employmentId) => setTarget({ mode: "create", employmentId }),
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
						{target ? <MandateHoursForm target={target} onDone={close} /> : null}
					</Modal.Container>
				</Modal.Backdrop>
			</Modal>
		);
	},
);

const NONE = "none";
const FORM_OPTIONS = [NONE, ...CONFIRMATION_FORMS] as const;

// Mirrors mandate_hours_validate: hours above 0 up to 744 (a month), confirmation date and form go together.
const schema = z
	.object({
		dateFrom: z.string().min(1, "required"),
		dateTo: z.string().min(1, "required"),
		hours: z
			.string()
			.trim()
			.min(1, "required")
			.refine((value) => {
				const hours = parseHours(value);
				return HOURS_PATTERN.test(value) && hours !== null && hours > 0 && hours <= 744;
			}, "invalid"),
		confirmedAt: z.string(),
		confirmationForm: z.enum(FORM_OPTIONS),
	})
	.superRefine((value, ctx) => {
		if (value.dateFrom && value.dateTo && value.dateTo < value.dateFrom) {
			ctx.addIssue({ code: "custom", path: ["dateTo"], message: "beforeFrom" });
		}
		if (value.confirmedAt && value.confirmationForm === NONE) {
			ctx.addIssue({ code: "custom", path: ["confirmationForm"], message: "required" });
		}
		if (!value.confirmedAt && value.confirmationForm !== NONE) {
			ctx.addIssue({ code: "custom", path: ["confirmedAt"], message: "required" });
		}
	});

const FIELDS = ["dateFrom", "dateTo", "hours", "confirmedAt", "confirmationForm"] as const;

function MandateHoursForm({ target, onDone }: { target: Target; onDone: () => void }) {
	const { t } = useTranslation();
	const formId = useId();
	const add = useAddMandateHours();
	const update = useUpdateMandateHours();
	const item = target.mode === "edit" ? target.item : null;

	const form = useAppForm({
		defaultValues: {
			dateFrom: item?.dateFrom ?? "",
			dateTo: item?.dateTo ?? "",
			hours: hoursText(item?.hours),
			confirmedAt: item?.confirmedAt ?? "",
			confirmationForm: (item?.confirmationForm ?? NONE) as (typeof FORM_OPTIONS)[number],
		},
		validators: { onChange: schema },
		onSubmit: async ({ value, formApi }) => {
			const request = {
				dateFrom: value.dateFrom,
				dateTo: value.dateTo,
				hours: parseHours(value.hours),
				confirmedAt: value.confirmedAt === "" ? null : value.confirmedAt,
				confirmationForm: value.confirmationForm === NONE ? null : value.confirmationForm,
			};
			try {
				if (item) {
					await update.mutateAsync({
						id: item.id,
						request: { rowVersion: item.rowVersion, ...request },
					});
					toast.success(t("timeAbsence.mandateHours.edit.saved"));
				} else if (target.mode === "create") {
					await add.mutateAsync({ employmentId: target.employmentId, request });
					toast.success(t("timeAbsence.mandateHours.create.created"));
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
						? t("timeAbsence.mandateHours.edit.title", {
								period: `${item.dateFrom} – ${item.dateTo}`,
							})
						: t("timeAbsence.mandateHours.create.title")}
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
					<div className="grid gap-4 sm:grid-cols-2">
						<form.AppField name="dateFrom">
							{(field) => (
								<field.DateInput label={t("timeAbsence.mandateHours.fields.dateFrom")} isRequired />
							)}
						</form.AppField>
						<form.AppField name="dateTo">
							{(field) => (
								<field.DateInput label={t("timeAbsence.mandateHours.fields.dateTo")} isRequired />
							)}
						</form.AppField>
						<form.AppField name="hours">
							{(field) => (
								<field.TextInput label={t("timeAbsence.mandateHours.fields.hours")} isRequired />
							)}
						</form.AppField>
					</div>
					<p className="text-sm text-muted">{t("timeAbsence.mandateHours.confirmationHint")}</p>
					<div className="grid gap-4 sm:grid-cols-2">
						<form.AppField name="confirmedAt">
							{(field) => (
								<field.DateInput label={t("timeAbsence.mandateHours.fields.confirmedAt")} />
							)}
						</form.AppField>
						<form.AppField name="confirmationForm">
							{(field) => (
								<field.SelectInput
									label={t("timeAbsence.mandateHours.fields.confirmationForm")}
									options={FORM_OPTIONS.map((option) => ({
										id: option,
										label:
											option === NONE
												? t("timeAbsence.mandateHours.noConfirmation")
												: t(`timeAbsence.mandateHours.forms.${option}`),
									}))}
								/>
							)}
						</form.AppField>
					</div>
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
