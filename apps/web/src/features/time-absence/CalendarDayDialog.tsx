import { Button, Modal, toast } from "@heroui/react";
import { forwardRef, useId, useImperativeHandle, useState } from "react";
import { useTranslation } from "react-i18next";
import { z } from "zod";
import type { GetCalendarMonthDay } from "#/api/models";
import { useAppForm } from "#/forms";
import { onSaveError } from "#/forms/feedback";
import { applyFieldErrors } from "#/forms/serverErrors";
import { useSetCalendarDay } from "./timeAbsenceQueries";
import {
	DAY_KINDS,
	type DayKind,
	dayKindsFor,
	HOURS_PATTERN,
	hoursText,
	parseHours,
} from "./types";

export type CalendarDayDialogHandle = {
	edit: (companyId: string, day: GetCalendarMonthDay) => void;
};

type Target = { companyId: string; day: GetCalendarMonthDay };

export const CalendarDayDialog = forwardRef<CalendarDayDialogHandle>(
	function CalendarDayDialog(_props, ref) {
		const [target, setTarget] = useState<Target | null>(null);
		useImperativeHandle(
			ref,
			() => ({ edit: (companyId, day) => setTarget({ companyId, day }) }),
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
						{target ? <DayForm target={target} onDone={close} /> : null}
					</Modal.Container>
				</Modal.Backdrop>
			</Modal>
		);
	},
);

// Hours only for a workday (0–12, calendar_day_set); other kinds are saved with 0.
const schema = z
	.object({ kind: z.enum(DAY_KINDS), hours: z.string() })
	.superRefine((value, ctx) => {
		if (value.kind !== "workday") return;
		if (value.hours.trim() === "") {
			ctx.addIssue({ code: "custom", path: ["hours"], message: "required" });
		} else if (!HOURS_PATTERN.test(value.hours) || (parseHours(value.hours) ?? 0) > 12) {
			ctx.addIssue({ code: "custom", path: ["hours"], message: "invalid" });
		}
	});

function DayForm({ target, onDone }: { target: Target; onDone: () => void }) {
	const { t } = useTranslation();
	const formId = useId();
	const save = useSetCalendarDay();
	const { day } = target;
	const isHoliday = day.holidayName !== null;

	const form = useAppForm({
		defaultValues: {
			kind: day.kind as DayKind,
			hours: day.kind === "workday" ? hoursText(day.hours) : "8",
		},
		validators: { onChange: schema },
		onSubmit: async ({ value, formApi }) => {
			try {
				await save.mutateAsync({
					companyId: target.companyId,
					date: day.date,
					request: {
						kind: value.kind,
						hours: value.kind === "workday" ? parseHours(value.hours) : 0,
					},
				});
				toast.success(t("timeAbsence.day.saved", { date: day.date }));
				onDone();
			} catch (error) {
				onSaveError(
					t,
					error,
					(errors) => applyFieldErrors(formApi, errors, ["kind", "hours"] as const),
					onDone,
				);
			}
		},
	});

	return (
		<Modal.Dialog className="form-dialog form-dialog--narrow">
			<Modal.Header>
				<Modal.Heading>{t("timeAbsence.day.title", { date: day.date })}</Modal.Heading>
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
					{isHoliday ? (
						<p className="text-sm text-muted">
							{t("timeAbsence.day.holidayHint", { name: day.holidayName ?? "" })}
						</p>
					) : null}
					<div className="grid gap-4 sm:grid-cols-2">
						<form.AppField name="kind">
							{(field) => (
								<field.SelectInput
									label={t("timeAbsence.day.kind")}
									options={dayKindsFor(day.date, isHoliday).map((kind) => ({
										id: kind,
										label: t(`timeAbsence.dayKinds.${kind}`),
									}))}
									isRequired
								/>
							)}
						</form.AppField>
						<form.Subscribe selector={(state) => state.values.kind}>
							{(kind) =>
								kind === "workday" ? (
									<form.AppField name="hours">
										{(field) => (
											<field.TextInput
												label={t("timeAbsence.day.hours")}
												description={t("timeAbsence.day.hoursHint")}
												isRequired
											/>
										)}
									</form.AppField>
								) : null
							}
						</form.Subscribe>
					</div>
					<form.Subscribe selector={(state) => state.values.kind}>
						{(kind) =>
							kind === "designatedOff" ? (
								<p className="text-sm text-muted">{t("timeAbsence.day.designatedHint")}</p>
							) : null
						}
					</form.Subscribe>
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
