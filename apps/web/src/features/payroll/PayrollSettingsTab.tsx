import { Button, EmptyState, Modal, Spinner, Table, toast } from "@heroui/react";
import { useQuery } from "@tanstack/react-query";
import { forwardRef, useId, useImperativeHandle, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { z } from "zod";
import type { ListPayrollSettingsItem } from "#/api/models";
import { AddButton, EditButton } from "#/components/ui/AddButton";
import { firstOfNextMonth, todayIso } from "#/features/ppk/types";
import { Notice } from "#/features/time-absence/Notice";
import { useAppForm } from "#/forms";
import { onSaveError, problemMessage } from "#/forms/feedback";
import { applyFieldErrors } from "#/forms/serverErrors";
import { payrollSettingsQuery, useAddPayrollSettings } from "./payrollQueries";
import { accidentRateFromPercent, accidentRatePercentText, formatLineRate } from "./types";

/** "Ustawienia płac": periods of the accident rate and of paying sickness allowance (R6, R13). */
export function PayrollSettingsTab({ companyId }: { companyId: string }) {
	const { t } = useTranslation();
	const dialog = useRef<SettingsDialogHandle>(null);
	const { data, isPending, error } = useQuery(payrollSettingsQuery(companyId));
	const items = [...(data ?? [])].sort((a, b) => b.validFrom.localeCompare(a.validFrom));
	// Only the latest period can change: an earlier start date is refused (beforeCurrent).
	const latest = items[0];

	return (
		<div className="flex flex-col gap-3">
			<p className="max-w-3xl text-sm text-muted">{t("payroll.settings.hint")}</p>
			<p className="max-w-3xl text-sm text-muted">{t("payroll.settings.defaultHint")}</p>
			<div className="flex gap-2">
				<AddButton
					label={t("payroll.settings.add.title")}
					onPress={() => dialog.current?.create(companyId, latest)}
				/>
			</div>
			{error ? <Notice status="danger">{problemMessage(t, error)}</Notice> : null}
			{isPending ? (
				<Spinner aria-label={t("common.loading")} />
			) : (
				<Table className="max-w-3xl">
					<Table.ScrollContainer>
						<Table.Content aria-label={t("payroll.tabs.settings")} className="min-w-[560px]">
							<Table.Header>
								<Table.Column isRowHeader>{t("payroll.settings.fields.validFrom")}</Table.Column>
								<Table.Column>{t("payroll.settings.fields.validTo")}</Table.Column>
								<Table.Column className="text-right">
									{t("payroll.settings.fields.accidentRate")}
								</Table.Column>
								<Table.Column>{t("payroll.settings.fields.paysSicknessAllowance")}</Table.Column>
								<Table.Column className="w-16">{t("common.actions")}</Table.Column>
							</Table.Header>
							<Table.Body
								renderEmptyState={() => (
									<EmptyState className="py-8 text-center text-sm text-muted">
										{t("payroll.settings.empty")}
									</EmptyState>
								)}
							>
								{items.map((item) => (
									<Table.Row key={item.id} id={item.id}>
										<Table.Cell>{item.validFrom}</Table.Cell>
										<Table.Cell>{item.validTo ?? "—"}</Table.Cell>
										<Table.Cell className="text-right">
											{formatLineRate(item.accidentRate)}
										</Table.Cell>
										<Table.Cell>
											{item.paysSicknessAllowance ? t("common.yes") : t("common.no")}
										</Table.Cell>
										<Table.Cell>
											{item.id === latest?.id ? (
												<EditButton
													size="sm"
													label={t("payroll.settings.edit.title", { date: item.validFrom })}
													onPress={() => dialog.current?.edit(companyId, item)}
												/>
											) : null}
										</Table.Cell>
									</Table.Row>
								))}
							</Table.Body>
						</Table.Content>
					</Table.ScrollContainer>
				</Table>
			)}
			<SettingsDialog ref={dialog} />
		</div>
	);
}

type Target = {
	companyId: string;
	/** Period being changed (same start date); without it a new period starts. */
	editing?: ListPayrollSettingsItem;
	/** Latest period, whose values a new period starts from. */
	latest?: ListPayrollSettingsItem;
};

type SettingsDialogHandle = {
	create: (companyId: string, latest?: ListPayrollSettingsItem) => void;
	edit: (companyId: string, item: ListPayrollSettingsItem) => void;
};

const SettingsDialog = forwardRef<SettingsDialogHandle>(function SettingsDialog(_props, ref) {
	const [target, setTarget] = useState<Target | null>(null);
	useImperativeHandle(
		ref,
		() => ({
			create: (companyId, latest) => setTarget({ companyId, latest }),
			edit: (companyId, item) => setTarget({ companyId, editing: item }),
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
					{target ? <SettingsForm target={target} onDone={close} /> : null}
				</Modal.Container>
			</Modal.Backdrop>
		</Modal>
	);
});

// Rules of payroll_app.company_settings_set: first day of a month, 0 < rate < 20 % with two decimals in percent.
const schema = z.object({
	validFrom: z
		.string()
		.min(1, "required")
		.refine((value) => value.endsWith("-01"), "invalid"),
	accidentRate: z
		.string()
		.trim()
		.min(1, "required")
		.refine((value) => {
			const rate = accidentRateFromPercent(value);
			return rate !== null && rate > 0 && rate < 0.2;
		}, "invalid"),
	paysSicknessAllowance: z.boolean(),
});

const FIELDS = ["validFrom", "accidentRate", "paysSicknessAllowance"] as const;

function SettingsForm({ target, onDone }: { target: Target; onDone: () => void }) {
	const { t } = useTranslation();
	const formId = useId();
	const save = useAddPayrollSettings();
	const source = target.editing ?? target.latest;
	// Editing keeps the start date, so its field is not shown and a code for it must surface as a toast.
	const fields = target.editing ? FIELDS.filter((field) => field !== "validFrom") : FIELDS;

	const form = useAppForm({
		defaultValues: {
			validFrom: target.editing?.validFrom ?? firstOfNextMonth(todayIso()),
			accidentRate: source ? accidentRatePercentText(source.accidentRate) : "1,67",
			paysSicknessAllowance: source?.paysSicknessAllowance ?? false,
		},
		validators: { onChange: schema },
		onSubmit: async ({ value, formApi }) => {
			try {
				await save.mutateAsync({
					companyId: target.companyId,
					request: {
						validFrom: value.validFrom,
						accidentRate: accidentRateFromPercent(value.accidentRate),
						paysSicknessAllowance: value.paysSicknessAllowance,
					},
				});
				toast.success(t("payroll.settings.add.saved", { date: value.validFrom }));
				onDone();
			} catch (error) {
				onSaveError(t, error, (errors) => applyFieldErrors(formApi, errors, fields), onDone);
			}
		},
	});

	return (
		<Modal.Dialog className="form-dialog">
			<Modal.Header>
				<Modal.Heading>
					{target.editing
						? t("payroll.settings.edit.title", { date: target.editing.validFrom })
						: t("payroll.settings.add.title")}
				</Modal.Heading>
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
					{target.editing ? null : (
						<form.AppField name="validFrom">
							{(field) => (
								<div className="flex flex-col gap-1">
									<field.DateInput label={t("payroll.settings.fields.validFrom")} isRequired />
									<span className="text-xs text-muted">{t("payroll.settings.validFromHint")}</span>
								</div>
							)}
						</form.AppField>
					)}
					<form.AppField name="accidentRate">
						{(field) => (
							<field.TextInput
								label={t("payroll.settings.fields.accidentRate")}
								description={t("payroll.settings.rateHint")}
								isRequired
							/>
						)}
					</form.AppField>
					<div className="sm:col-span-2">
						<form.AppField name="paysSicknessAllowance">
							{(field) => (
								<field.CheckboxInput label={t("payroll.settings.fields.paysSicknessAllowance")} />
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
