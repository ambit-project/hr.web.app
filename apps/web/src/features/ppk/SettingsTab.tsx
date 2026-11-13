import { Button, EmptyState, Modal, Spinner, Table, toast } from "@heroui/react";
import { useQuery } from "@tanstack/react-query";
import { forwardRef, useId, useImperativeHandle, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { z } from "zod";
import type { ListPpkSettingsItem } from "#/api/models";
import { AddButton, EditButton } from "#/components/ui/AddButton";
import { text, useAppForm } from "#/forms";
import { onSaveError } from "#/forms/feedback";
import { applyFieldErrors } from "#/forms/serverErrors";
import { ppkSettingsQuery, useAddPpkSettings } from "./ppkQueries";
import { firstOfNextMonth, formatRate, parseRate, rateText, rateWithin, todayIso } from "./types";

/** "Ustawienia": periods of the management agreement and the employer's additional rate (R1). */
export function SettingsTab({ companyId }: { companyId: string }) {
	const { t } = useTranslation();
	const dialog = useRef<SettingsDialogHandle>(null);
	const { data, isPending } = useQuery(ppkSettingsQuery(companyId));
	const items = [...(data ?? [])].sort((a, b) => b.validFrom.localeCompare(a.validFrom));
	// Only the latest period can change: an earlier start date is refused (beforeCurrent).
	const latest = items[0];

	return (
		<div className="flex flex-col gap-3">
			<p className="max-w-3xl text-sm text-muted">{t("ppk.settings.hint")}</p>
			<p className="max-w-3xl text-sm text-muted">{t("ppk.settings.employerBasic")}</p>
			<div className="flex gap-2">
				<AddButton
					label={t("ppk.settings.add.title")}
					onPress={() => dialog.current?.create(companyId, latest)}
				/>
			</div>
			{isPending ? (
				<Spinner aria-label={t("common.loading")} />
			) : (
				<Table className="max-w-5xl">
					<Table.ScrollContainer>
						<Table.Content aria-label={t("ppk.tabs.settings")} className="min-w-[820px]">
							<Table.Header>
								<Table.Column isRowHeader>{t("ppk.settings.fields.validFrom")}</Table.Column>
								<Table.Column>{t("ppk.settings.fields.validTo")}</Table.Column>
								<Table.Column>{t("ppk.settings.fields.institutionName")}</Table.Column>
								<Table.Column>{t("ppk.settings.fields.agreementNumber")}</Table.Column>
								<Table.Column>{t("ppk.settings.fields.agreementDate")}</Table.Column>
								<Table.Column className="text-right">
									{t("ppk.settings.fields.employerAdditionalRate")}
								</Table.Column>
								<Table.Column>{t("common.actions")}</Table.Column>
							</Table.Header>
							<Table.Body
								renderEmptyState={() => (
									<EmptyState className="py-8 text-center text-sm text-muted">
										{t("ppk.settings.empty")}
									</EmptyState>
								)}
							>
								{items.map((item) => (
									<Table.Row key={item.id} id={item.id}>
										<Table.Cell>{item.validFrom}</Table.Cell>
										<Table.Cell>{item.validTo ?? "—"}</Table.Cell>
										<Table.Cell>{item.institutionName}</Table.Cell>
										<Table.Cell>{item.agreementNumber}</Table.Cell>
										<Table.Cell>{item.agreementDate}</Table.Cell>
										<Table.Cell className="text-right">
											{formatRate(item.employerAdditionalRate)}
										</Table.Cell>
										<Table.Cell>
											{item.id === latest?.id ? (
												<EditButton
													size="sm"
													label={t("ppk.settings.edit.title", { date: item.validFrom })}
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
	editing?: ListPpkSettingsItem;
	/** Latest period, whose agreement a new period usually repeats. */
	latest?: ListPpkSettingsItem;
};

type SettingsDialogHandle = {
	create: (companyId: string, latest?: ListPpkSettingsItem) => void;
	edit: (companyId: string, item: ListPpkSettingsItem) => void;
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

// Rules of ppk_app.settings_add: first day of a month, agreement not from the future, 0–2.5 %.
const schema = z.object({
	validFrom: z
		.string()
		.min(1, "required")
		.refine((value) => value.endsWith("-01"), "invalid"),
	institutionName: text({ required: true, max: 200 }),
	agreementNumber: text({ required: true, max: 60 }),
	agreementDate: z
		.string()
		.min(1, "required")
		.refine((value) => value <= todayIso(), "future"),
	employerAdditionalRate: z
		.string()
		.trim()
		.min(1, "required")
		.refine((value) => rateWithin(value, 0, 2.5), "invalid"),
});

const FIELDS = [
	"validFrom",
	"institutionName",
	"agreementNumber",
	"agreementDate",
	"employerAdditionalRate",
] as const;

function SettingsForm({ target, onDone }: { target: Target; onDone: () => void }) {
	const { t } = useTranslation();
	const formId = useId();
	const save = useAddPpkSettings();
	const source = target.editing ?? target.latest;
	// Editing keeps the start date, so its field is not shown and a code for it must surface as a toast.
	const fields = target.editing ? FIELDS.filter((field) => field !== "validFrom") : FIELDS;

	const form = useAppForm({
		defaultValues: {
			validFrom: target.editing?.validFrom ?? firstOfNextMonth(todayIso()),
			institutionName: source?.institutionName ?? "",
			agreementNumber: source?.agreementNumber ?? "",
			agreementDate: source?.agreementDate ?? "",
			employerAdditionalRate: source ? rateText(source.employerAdditionalRate) : "0",
		},
		validators: { onChange: schema },
		onSubmit: async ({ value, formApi }) => {
			try {
				await save.mutateAsync({
					companyId: target.companyId,
					request: {
						validFrom: value.validFrom,
						institutionName: value.institutionName.trim(),
						agreementNumber: value.agreementNumber.trim(),
						agreementDate: value.agreementDate,
						employerAdditionalRate: parseRate(value.employerAdditionalRate),
					},
				});
				toast.success(t("ppk.settings.add.saved", { date: value.validFrom }));
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
						? t("ppk.settings.edit.title", { date: target.editing.validFrom })
						: t("ppk.settings.add.title")}
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
									<field.DateInput label={t("ppk.settings.fields.validFrom")} isRequired />
									<span className="text-xs text-muted">{t("ppk.settings.validFromHint")}</span>
								</div>
							)}
						</form.AppField>
					)}
					<form.AppField name="employerAdditionalRate">
						{(field) => (
							<field.TextInput
								label={t("ppk.settings.fields.employerAdditionalRate")}
								description={t("ppk.settings.rateHint")}
								isRequired
							/>
						)}
					</form.AppField>
					<div className="sm:col-span-2">
						<form.AppField name="institutionName">
							{(field) => (
								<field.TextInput label={t("ppk.settings.fields.institutionName")} isRequired />
							)}
						</form.AppField>
					</div>
					<form.AppField name="agreementNumber">
						{(field) => (
							<field.TextInput label={t("ppk.settings.fields.agreementNumber")} isRequired />
						)}
					</form.AppField>
					<form.AppField name="agreementDate">
						{(field) => (
							<field.DateInput label={t("ppk.settings.fields.agreementDate")} isRequired />
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
