import { Button, Modal, toast } from "@heroui/react";
import { forwardRef, useId, useImperativeHandle, useState } from "react";
import { useTranslation } from "react-i18next";
import { z } from "zod";
import { text, useAppForm } from "#/forms";
import { onSaveError } from "#/forms/feedback";
import { applyFieldErrors } from "#/forms/serverErrors";
import { useAddHoliday } from "./timeAbsenceQueries";

export type HolidayDialogHandle = { create: (year: number) => void };

export const HolidayDialog = forwardRef<HolidayDialogHandle>(function HolidayDialog(_props, ref) {
	const [year, setYear] = useState<number | null>(null);
	useImperativeHandle(ref, () => ({ create: setYear }), []);
	const close = () => setYear(null);

	return (
		<Modal>
			<Modal.Backdrop
				isDismissable={false}
				isKeyboardDismissDisabled
				isOpen={year !== null}
				onOpenChange={(open) => !open && close()}
			>
				<Modal.Container scroll="inside">
					{year !== null ? <HolidayForm year={year} onDone={close} /> : null}
				</Modal.Container>
			</Modal.Backdrop>
		</Modal>
	);
});

const schema = z.object({
	date: z.string().min(1, "required"),
	name: text({ required: true, max: 100 }),
});

function HolidayForm({ year, onDone }: { year: number; onDone: () => void }) {
	const { t } = useTranslation();
	const formId = useId();
	const add = useAddHoliday();

	const form = useAppForm({
		defaultValues: { date: `${year}-01-01`, name: "" },
		validators: { onChange: schema },
		onSubmit: async ({ value, formApi }) => {
			try {
				await add.mutateAsync({ date: value.date, name: value.name.trim() });
				toast.success(t("timeAbsence.holidays.add.added", { date: value.date }));
				onDone();
			} catch (error) {
				onSaveError(
					t,
					error,
					(errors) => applyFieldErrors(formApi, errors, ["date", "name"] as const),
					onDone,
				);
			}
		},
	});

	return (
		<Modal.Dialog className="form-dialog">
			<Modal.Header>
				<Modal.Heading>{t("timeAbsence.holidays.add.title")}</Modal.Heading>
			</Modal.Header>
			<Modal.Body>
				<form
					id={formId}
					className="grid gap-4 sm:grid-cols-2"
					noValidate
					onSubmit={(event) => {
						event.preventDefault();
						void form.handleSubmit();
					}}
				>
					<form.AppField name="date">
						{(field) => <field.DateInput label={t("timeAbsence.holidays.date")} isRequired />}
					</form.AppField>
					<div className="sm:col-span-2">
						<form.AppField name="name">
							{(field) => <field.TextInput label={t("timeAbsence.holidays.name")} isRequired />}
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
