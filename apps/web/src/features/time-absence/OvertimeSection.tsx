import { Button, EmptyState, Modal, Spinner, Table, toast } from "@heroui/react";
import { useQuery } from "@tanstack/react-query";
import { forwardRef, useId, useImperativeHandle, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { z } from "zod";
import type { ListOvertimeEntry } from "#/api/models";
import { AddButton } from "#/components/ui/AddButton";
import { ConfirmDelete } from "#/components/ui/ConfirmDelete";
import { useAppForm } from "#/forms";
import { onActionError, onSaveError, problemMessage } from "#/forms/feedback";
import { applyFieldErrors } from "#/forms/serverErrors";
import { Notice } from "./Notice";
import { overtimeQuery, useAddOvertime, useDeleteOvertime } from "./timeAbsenceQueries";
import { formatHours, HOURS_PATTERN, OVERTIME_RATES, type OvertimeRate, parseHours } from "./types";

/** "Nadgodziny" of an employment contract: entries of the year, the year's sum and the 150 h warning. */
export function OvertimeSection({ employmentId, year }: { employmentId: string; year: number }) {
	const { t } = useTranslation();
	const dialog = useRef<OvertimeDialogHandle>(null);
	const { data, isPending, error } = useQuery(overtimeQuery(employmentId, year));

	return (
		<div className="flex flex-col gap-3">
			<h3 className="text-base font-semibold">{t("timeAbsence.overtime.title")}</h3>
			<p className="max-w-3xl text-sm text-muted">{t("timeAbsence.overtime.hint")}</p>
			<div className="flex gap-2">
				<AddButton
					label={t("timeAbsence.overtime.add.title")}
					onPress={() => dialog.current?.create(employmentId)}
				/>
			</div>
			{isPending ? (
				<Spinner aria-label={t("common.loading")} />
			) : error || !data ? (
				<Notice status="danger">{problemMessage(t, error)}</Notice>
			) : (
				<>
					{data.aboveLimit ? (
						<Notice>{t("timeAbsence.overtime.aboveLimit", { limit: data.limitHours })}</Notice>
					) : null}
					<p className="text-sm tabular-nums">
						{t("timeAbsence.overtime.total", {
							total: formatHours(data.totalHours),
							h50: formatHours(data.hours50),
							h100: formatHours(data.hours100),
						})}
					</p>
					<Table className="max-w-3xl">
						<Table.ScrollContainer>
							<Table.Content aria-label={t("timeAbsence.overtime.title")} className="min-w-[480px]">
								<Table.Header>
									<Table.Column isRowHeader>{t("timeAbsence.overtime.fields.date")}</Table.Column>
									<Table.Column className="text-right">
										{t("timeAbsence.overtime.fields.hours")}
									</Table.Column>
									<Table.Column>{t("timeAbsence.overtime.fields.rate")}</Table.Column>
									<Table.Column>{t("common.actions")}</Table.Column>
								</Table.Header>
								<Table.Body
									renderEmptyState={() => (
										<EmptyState className="py-8 text-center text-sm text-muted">
											{t("timeAbsence.overtime.empty")}
										</EmptyState>
									)}
								>
									{data.entries.map((entry) => (
										<Table.Row key={entry.id} id={entry.id}>
											<Table.Cell>{entry.date}</Table.Cell>
											<Table.Cell className="text-right">{formatHours(entry.hours)}</Table.Cell>
											<Table.Cell>
												{t(`timeAbsence.overtime.rates.${String(entry.rate) as OvertimeRate}`)}
											</Table.Cell>
											<Table.Cell>
												<DeleteOvertime entry={entry} />
											</Table.Cell>
										</Table.Row>
									))}
								</Table.Body>
							</Table.Content>
						</Table.ScrollContainer>
					</Table>
				</>
			)}
			<OvertimeDialog ref={dialog} />
		</div>
	);
}

function DeleteOvertime({ entry }: { entry: ListOvertimeEntry }) {
	const { t } = useTranslation();
	const remove = useDeleteOvertime();

	const onDelete = async () => {
		try {
			await remove.mutateAsync({ id: entry.id, rowVersion: entry.rowVersion });
			toast.success(t("timeAbsence.overtime.delete.deleted"));
		} catch (error) {
			onActionError(t, error);
		}
	};

	return (
		<ConfirmDelete
			label={t("timeAbsence.overtime.delete.action")}
			title={t("timeAbsence.overtime.delete.title")}
			body={t("timeAbsence.overtime.delete.body", {
				date: entry.date,
				hours: formatHours(entry.hours),
			})}
			onConfirm={() => void onDelete()}
		/>
	);
}

type OvertimeDialogHandle = { create: (employmentId: string) => void };

const OvertimeDialog = forwardRef<OvertimeDialogHandle>(function OvertimeDialog(_props, ref) {
	const [employmentId, setEmploymentId] = useState<string | null>(null);
	useImperativeHandle(ref, () => ({ create: setEmploymentId }), []);
	const close = () => setEmploymentId(null);

	return (
		<Modal>
			<Modal.Backdrop
				isDismissable={false}
				isKeyboardDismissDisabled
				isOpen={employmentId !== null}
				onOpenChange={(open) => !open && close()}
			>
				<Modal.Container scroll="inside">
					{employmentId ? <OvertimeForm employmentId={employmentId} onDone={close} /> : null}
				</Modal.Container>
			</Modal.Backdrop>
		</Modal>
	);
});

// 0.5–16 h (overtime_add); the database stays the authority.
const schema = z.object({
	date: z.string().min(1, "required"),
	hours: z
		.string()
		.trim()
		.min(1, "required")
		.refine((value) => {
			const hours = parseHours(value);
			return HOURS_PATTERN.test(value) && hours !== null && hours >= 0.5 && hours <= 16;
		}, "invalid"),
	rate: z.enum(OVERTIME_RATES),
});

function OvertimeForm({ employmentId, onDone }: { employmentId: string; onDone: () => void }) {
	const { t } = useTranslation();
	const formId = useId();
	const add = useAddOvertime();

	const form = useAppForm({
		defaultValues: { date: "", hours: "", rate: "50" as OvertimeRate },
		validators: { onChange: schema },
		onSubmit: async ({ value, formApi }) => {
			try {
				await add.mutateAsync({
					employmentId,
					request: {
						date: value.date,
						hours: parseHours(value.hours),
						rate: Number(value.rate),
					},
				});
				toast.success(t("timeAbsence.overtime.add.added"));
				onDone();
			} catch (error) {
				onSaveError(
					t,
					error,
					(errors) => applyFieldErrors(formApi, errors, ["date", "hours", "rate"] as const),
					onDone,
				);
			}
		},
	});

	return (
		<Modal.Dialog className="form-dialog">
			<Modal.Header>
				<Modal.Heading>{t("timeAbsence.overtime.add.title")}</Modal.Heading>
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
						<form.AppField name="date">
							{(field) => (
								<field.DateInput label={t("timeAbsence.overtime.fields.date")} isRequired />
							)}
						</form.AppField>
						<form.AppField name="hours">
							{(field) => (
								<field.TextInput
									label={t("timeAbsence.overtime.fields.hours")}
									description={t("timeAbsence.overtime.hoursHint")}
									isRequired
								/>
							)}
						</form.AppField>
						<form.AppField name="rate">
							{(field) => (
								<field.SelectInput
									label={t("timeAbsence.overtime.fields.rate")}
									options={OVERTIME_RATES.map((rate) => ({
										id: rate,
										label: t(`timeAbsence.overtime.rates.${rate}`),
									}))}
									isRequired
								/>
							)}
						</form.AppField>
					</div>
					<p className="text-sm text-muted">{t("timeAbsence.overtime.hint")}</p>
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
