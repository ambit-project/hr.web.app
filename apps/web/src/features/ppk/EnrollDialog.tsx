import { Button, Modal, toast } from "@heroui/react";
import { forwardRef, useId, useImperativeHandle, useState } from "react";
import { useTranslation } from "react-i18next";
import { z } from "zod";
import { Notice } from "#/features/time-absence/Notice";
import { text, useAppForm } from "#/forms";
import { onSaveError } from "#/forms/feedback";
import { applyFieldErrors } from "#/forms/serverErrors";
import { useEnrollInPpk } from "./ppkQueries";
import { parseRate, rateWithin, todayIso } from "./types";

type Target = { employeeId: string; name: string; deadline?: string; requestOnly?: boolean };

export type EnrollDialogHandle = { enroll: (target: Target) => void };

/** Enrolment in PPK (R6, R7): from the candidate list or from the employee's PPK tab. */
export const EnrollDialog = forwardRef<EnrollDialogHandle>(function EnrollDialog(_props, ref) {
	const [target, setTarget] = useState<Target | null>(null);
	useImperativeHandle(ref, () => ({ enroll: setTarget }), []);
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
					{target ? <EnrollForm target={target} onDone={close} /> : null}
				</Modal.Container>
			</Modal.Backdrop>
		</Modal>
	);
});

// Basic 0.5–2 %, additional 0–2 % (ppk_app.participation_create, art. 27 ust. 2–3); the database stays the authority.
const schema = z.object({
	enrolledAt: z.string().min(1, "required"),
	institutionMemberNumber: text({ max: 40 }),
	employeeBasicRate: z
		.string()
		.trim()
		.min(1, "required")
		.refine((value) => rateWithin(value, 0.5, 2), "invalid"),
	employeeAdditionalRate: z
		.string()
		.refine((value) => value.trim() === "" || rateWithin(value, 0, 2), "invalid"),
});

const FIELDS = [
	"enrolledAt",
	"institutionMemberNumber",
	"employeeBasicRate",
	"employeeAdditionalRate",
] as const;

function EnrollForm({ target, onDone }: { target: Target; onDone: () => void }) {
	const { t } = useTranslation();
	const formId = useId();
	const enroll = useEnrollInPpk();

	const form = useAppForm({
		defaultValues: {
			enrolledAt: todayIso(),
			institutionMemberNumber: "",
			employeeBasicRate: "2",
			employeeAdditionalRate: "0",
		},
		validators: { onChange: schema },
		onSubmit: async ({ value, formApi }) => {
			try {
				await enroll.mutateAsync({
					employeeId: target.employeeId,
					request: {
						enrolledAt: value.enrolledAt,
						institutionMemberNumber: value.institutionMemberNumber.trim() || null,
						employeeBasicRate: parseRate(value.employeeBasicRate),
						employeeAdditionalRate: parseRate(value.employeeAdditionalRate) ?? 0,
					},
				});
				toast.success(t("ppk.enroll.saved"));
				onDone();
			} catch (error) {
				onSaveError(t, error, (errors) => applyFieldErrors(formApi, errors, FIELDS), onDone);
			}
		},
	});

	return (
		<Modal.Dialog className="form-dialog">
			<Modal.Header>
				<Modal.Heading>{t("ppk.enroll.title", { name: target.name })}</Modal.Heading>
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
					{target.requestOnly ? <Notice>{t("ppk.candidates.requestOnlyHint")}</Notice> : null}
					{target.deadline ? (
						<p className="text-sm text-muted">
							{t("ppk.enroll.deadlineHint", { deadline: target.deadline })}
						</p>
					) : null}
					<div className="grid gap-4 sm:grid-cols-2">
						<form.AppField name="enrolledAt">
							{(field) => <field.DateInput label={t("ppk.enroll.fields.enrolledAt")} isRequired />}
						</form.AppField>
						<form.AppField name="institutionMemberNumber">
							{(field) => (
								<field.TextInput label={t("ppk.enroll.fields.institutionMemberNumber")} />
							)}
						</form.AppField>
						<form.AppField name="employeeBasicRate">
							{(field) => (
								<field.TextInput
									label={t("ppk.enroll.fields.employeeBasicRate")}
									description={t("ppk.enroll.basicHint")}
									isRequired
								/>
							)}
						</form.AppField>
						<form.AppField name="employeeAdditionalRate">
							{(field) => (
								<field.TextInput
									label={t("ppk.enroll.fields.employeeAdditionalRate")}
									description={t("ppk.enroll.additionalHint")}
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
