import { Button, Modal, Spinner, toast } from "@heroui/react";
import { useQuery } from "@tanstack/react-query";
import { forwardRef, useId, useImperativeHandle, useState } from "react";
import { useTranslation } from "react-i18next";
import { z } from "zod";
import type { GetLeaveEntitlementResponse } from "#/api/models";
import { useAppForm } from "#/forms";
import { onSaveError } from "#/forms/feedback";
import { applyFieldErrors } from "#/forms/serverErrors";
import { leaveQuery, useSetLeave } from "./timeAbsenceQueries";
import {
	formatHours,
	formatNumber,
	HOURS_PATTERN,
	hoursText,
	hoursToDays,
	LEAVE_BASES,
	type LeaveBase,
	parseHours,
} from "./types";

export type LeaveDialogHandle = {
	edit: (employmentId: string, year: number, leave: GetLeaveEntitlementResponse) => void;
};

type Target = { employmentId: string; year: number; leave: GetLeaveEntitlementResponse };

export const LeaveDialog = forwardRef<LeaveDialogHandle>(function LeaveDialog(_props, ref) {
	const [target, setTarget] = useState<Target | null>(null);
	useImperativeHandle(
		ref,
		() => ({ edit: (employmentId, year, leave) => setTarget({ employmentId, year, leave }) }),
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
					{target ? <LeaveForm target={target} onDone={close} /> : null}
				</Modal.Container>
			</Modal.Backdrop>
		</Modal>
	);
});

const hours = z
	.string()
	.refine((value) => value.trim() === "" || HOURS_PATTERN.test(value), "invalid");

// A corrected limit or carried-over leave needs a reason (leave_entitlement_set).
const schema = z
	.object({
		baseDays: z.enum(LEAVE_BASES),
		firstJob: z.boolean(),
		entitledHours: hours,
		carriedOverHours: hours,
		overrideReason: z.string().max(500, "maxLength"),
	})
	.superRefine((value, ctx) => {
		const overridden = value.entitledHours.trim() !== "" || value.carriedOverHours.trim() !== "";
		if (overridden && value.overrideReason.trim() === "") {
			ctx.addIssue({ code: "custom", path: ["overrideReason"], message: "required" });
		}
	});

const FIELDS = [
	"baseDays",
	"firstJob",
	"entitledHours",
	"carriedOverHours",
	"overrideReason",
] as const;

function LeaveForm({ target, onDone }: { target: Target; onDone: () => void }) {
	const { t } = useTranslation();
	const formId = useId();
	const save = useSetLeave();
	const { leave } = target;

	const form = useAppForm({
		defaultValues: {
			baseDays: String(leave.baseDays ?? 20) as LeaveBase,
			firstJob: leave.firstJob ?? false,
			entitledHours: leave.entitledOverridden ? hoursText(leave.entitledHours) : "",
			carriedOverHours: leave.carriedOverOverridden ? hoursText(leave.carriedOverHours) : "",
			overrideReason: leave.overrideReason ?? "",
		},
		validators: { onChange: schema },
		onSubmit: async ({ value, formApi }) => {
			const entitledHours = parseHours(value.entitledHours);
			const carriedOverHours = parseHours(value.carriedOverHours);
			try {
				await save.mutateAsync({
					id: target.employmentId,
					year: target.year,
					request: {
						baseDays: Number(value.baseDays),
						firstJob: value.firstJob,
						entitledHours,
						carriedOverHours,
						overrideReason:
							entitledHours === null && carriedOverHours === null
								? null
								: value.overrideReason.trim(),
						rowVersion: leave.rowVersion,
					},
				});
				toast.success(t("timeAbsence.leave.set.saved"));
				onDone();
			} catch (error) {
				onSaveError(t, error, (errors) => applyFieldErrors(formApi, errors, FIELDS), onDone);
			}
		},
	});

	return (
		<Modal.Dialog className="form-dialog">
			<Modal.Header>
				<Modal.Heading>{t("timeAbsence.leave.set.title", { year: target.year })}</Modal.Heading>
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
					<form.AppField name="baseDays">
						{(field) => (
							<field.SelectInput
								label={t("timeAbsence.leave.fields.baseDays")}
								options={LEAVE_BASES.map((base) => ({
									id: base,
									label: t(`timeAbsence.leave.bases.${base}`),
								}))}
								isRequired
							/>
						)}
					</form.AppField>
					<form.AppField name="firstJob">
						{(field) => <field.CheckboxInput label={t("timeAbsence.leave.fields.firstJob")} />}
					</form.AppField>
					<form.Subscribe
						selector={(state) => [state.values.baseDays, state.values.firstJob] as const}
					>
						{([baseDays, firstJob]) => (
							<Proposal
								employmentId={target.employmentId}
								year={target.year}
								baseDays={Number(baseDays)}
								firstJob={firstJob}
							/>
						)}
					</form.Subscribe>
					<p className="text-sm text-muted">{t("timeAbsence.leave.overrideHint")}</p>
					<div className="grid gap-4 sm:grid-cols-2">
						<form.AppField name="entitledHours">
							{(field) => <field.TextInput label={t("timeAbsence.leave.fields.entitledHours")} />}
						</form.AppField>
						<form.AppField name="carriedOverHours">
							{(field) => (
								<field.TextInput label={t("timeAbsence.leave.fields.carriedOverHours")} />
							)}
						</form.AppField>
					</div>
					<form.AppField name="overrideReason">
						{(field) => (
							<field.TextInput label={t("timeAbsence.leave.fields.overrideReason")} multiline />
						)}
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

/** Proposal from the Labour Code for the base and first-job flag chosen in the form, asked from the database. */
function Proposal(props: {
	employmentId: string;
	year: number;
	baseDays: number;
	firstJob: boolean;
}) {
	const { t } = useTranslation();
	const { data, isPending } = useQuery(
		leaveQuery(props.employmentId, props.year, {
			baseDays: props.baseDays,
			firstJob: props.firstJob,
		}),
	);
	if (isPending) return <Spinner size="sm" aria-label={t("common.loading")} />;
	if (!data) return null;
	return (
		<p className="text-sm font-medium">
			{t("timeAbsence.leave.proposalLine", {
				hours: formatHours(data.proposedHours),
				days: formatNumber(hoursToDays(data.proposedHours)),
			})}
		</p>
	);
}
