import { Button, Modal, toast } from "@heroui/react";
import { forwardRef, useId, useImperativeHandle, useState } from "react";
import { useTranslation } from "react-i18next";
import { z } from "zod";
import type { ListGarnishmentsItem } from "#/api/models";
import { text, useAppForm } from "#/forms";
import { applyFieldErrors } from "#/forms/serverErrors";
import { AMOUNT_PATTERN, amountText } from "#/lib/money";
import { onSaveError } from "./feedback";
import {
	useCreateGarnishment,
	useSetGarnishmentStatus,
	useUpdateGarnishment,
} from "./payComponentsQueries";
import {
	authorityKindsFor,
	GARNISHMENT_KINDS,
	GARNISHMENT_STATUSES,
	type GarnishmentKind,
	type GarnishmentStatus,
	garnishmentRequest,
} from "./types";

export type GarnishmentDialogHandle = {
	create: (employeeId: string) => void;
	edit: (item: ListGarnishmentsItem) => void;
	setStatus: (item: ListGarnishmentsItem) => void;
};

type Target =
	| { mode: "create"; employeeId: string }
	| { mode: "edit"; item: ListGarnishmentsItem }
	| { mode: "status"; item: ListGarnishmentsItem };

export const GarnishmentDialog = forwardRef<GarnishmentDialogHandle>(
	function GarnishmentDialog(_props, ref) {
		const [target, setTarget] = useState<Target | null>(null);
		useImperativeHandle(
			ref,
			() => ({
				create: (employeeId) => setTarget({ mode: "create", employeeId }),
				edit: (item) => setTarget({ mode: "edit", item }),
				setStatus: (item) => setTarget({ mode: "status", item }),
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
						{target?.mode === "status" ? (
							<StatusForm item={target.item} onDone={close} />
						) : target ? (
							<GarnishmentForm target={target} onDone={close} />
						) : null}
					</Modal.Container>
				</Modal.Backdrop>
			</Modal>
		);
	},
);

const amount = z
	.string()
	.refine((value) => value.trim() === "" || AMOUNT_PATTERN.test(value), "invalid");

const schema = z.object({
	kind: z.enum(GARNISHMENT_KINDS),
	authorityKind: z.string().min(1, "required"),
	authorityName: text({ required: true, max: 300 }),
	caseNumber: text({ required: true, max: 100 }),
	receivedAt: z.string().min(1, "required"),
	monthlyAmount: amount,
	totalAmount: amount,
	note: text({ max: 2000 }),
});

const FIELDS = [
	"kind",
	"authorityKind",
	"authorityName",
	"caseNumber",
	"receivedAt",
	"monthlyAmount",
	"totalAmount",
	"note",
] as const;

function GarnishmentForm({
	target,
	onDone,
}: {
	target: Exclude<Target, { mode: "status" }>;
	onDone: () => void;
}) {
	const { t } = useTranslation();
	const formId = useId();
	const create = useCreateGarnishment();
	const update = useUpdateGarnishment();
	const item = target.mode === "edit" ? target.item : null;

	const form = useAppForm({
		defaultValues: {
			kind: (item?.kind ?? "other") as GarnishmentKind,
			authorityKind: item?.authorityKind ?? "court",
			authorityName: item?.authorityName ?? "",
			caseNumber: item?.caseNumber ?? "",
			receivedAt: item?.receivedAt ?? "",
			monthlyAmount: amountText(item?.monthlyAmount),
			totalAmount: amountText(item?.totalAmount),
			note: item?.note ?? "",
		},
		validators: { onChange: schema },
		onSubmit: async ({ value, formApi }) => {
			try {
				if (item) {
					await update.mutateAsync({
						id: item.id,
						request: { rowVersion: item.rowVersion, ...garnishmentRequest(value) },
					});
					toast.success(t("payComponents.garnishments.edit.saved"));
				} else if (target.mode === "create") {
					await create.mutateAsync({
						employeeId: target.employeeId,
						request: garnishmentRequest(value),
					});
					toast.success(t("payComponents.garnishments.create.created"));
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
						? t("payComponents.garnishments.edit.title", { caseNumber: item.caseNumber })
						: t("payComponents.garnishments.create.title")}
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
						<form.AppField
							name="kind"
							listeners={{
								// A creditor collects without enforcement only alimony (KP art. 88).
								onChange: ({ value }) => {
									if (
										!(authorityKindsFor(value) as string[]).includes(
											form.getFieldValue("authorityKind"),
										)
									)
										form.setFieldValue("authorityKind", "court");
								},
							}}
						>
							{(field) => (
								<field.SelectInput
									label={t("payComponents.garnishments.fields.kind")}
									options={GARNISHMENT_KINDS.map((kind) => ({
										id: kind,
										label: t(`payComponents.garnishments.kinds.${kind}`),
									}))}
									isRequired
								/>
							)}
						</form.AppField>
						<form.Subscribe selector={(state) => state.values.kind}>
							{(kind) => (
								<form.AppField name="authorityKind">
									{(field) => (
										<field.SelectInput
											label={t("payComponents.garnishments.fields.authorityKind")}
											options={authorityKindsFor(kind).map((authorityKind) => ({
												id: authorityKind,
												label: t(`payComponents.garnishments.authorityKinds.${authorityKind}`),
											}))}
											isRequired
										/>
									)}
								</form.AppField>
							)}
						</form.Subscribe>
					</div>
					<form.AppField name="authorityName">
						{(field) => (
							<field.TextInput
								label={t("payComponents.garnishments.fields.authorityName")}
								isRequired
							/>
						)}
					</form.AppField>
					<div className="grid gap-4 sm:grid-cols-2">
						<form.AppField name="caseNumber">
							{(field) => (
								<field.TextInput
									label={t("payComponents.garnishments.fields.caseNumber")}
									isRequired
								/>
							)}
						</form.AppField>
						<form.AppField name="receivedAt">
							{(field) => (
								<field.DateInput
									label={t("payComponents.garnishments.fields.receivedAt")}
									isRequired
								/>
							)}
						</form.AppField>
					</div>
					<div className="grid gap-4 sm:grid-cols-2">
						<form.AppField name="monthlyAmount">
							{(field) => (
								<field.TextInput label={t("payComponents.garnishments.fields.monthlyAmount")} />
							)}
						</form.AppField>
						<form.AppField name="totalAmount">
							{(field) => (
								<field.TextInput label={t("payComponents.garnishments.fields.totalAmount")} />
							)}
						</form.AppField>
					</div>
					<p className="text-sm text-muted">{t("payComponents.garnishments.amountsHint")}</p>
					<form.AppField name="note">
						{(field) => (
							<field.TextInput label={t("payComponents.garnishments.fields.note")} multiline />
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

const statusSchema = z.object({ status: z.enum(GARNISHMENT_STATUSES), statusDate: z.string() });

function StatusForm({ item, onDone }: { item: ListGarnishmentsItem; onDone: () => void }) {
	const { t } = useTranslation();
	const formId = useId();
	const setStatus = useSetGarnishmentStatus();

	const form = useAppForm({
		defaultValues: {
			status: item.status as GarnishmentStatus,
			statusDate: item.statusDate ?? "",
		},
		validators: { onChange: statusSchema },
		onSubmit: async ({ value, formApi }) => {
			try {
				await setStatus.mutateAsync({
					id: item.id,
					request: {
						rowVersion: item.rowVersion,
						status: value.status,
						// An active garnishment has no status date.
						statusDate:
							value.status === "active" || value.statusDate === "" ? null : value.statusDate,
					},
				});
				toast.success(t("payComponents.garnishments.setStatus.saved"));
				onDone();
			} catch (error) {
				onSaveError(
					t,
					error,
					(errors) => applyFieldErrors(formApi, errors, ["status", "statusDate"] as const),
					onDone,
				);
			}
		},
	});

	return (
		<Modal.Dialog className="form-dialog">
			<Modal.Header>
				<Modal.Heading>
					{t("payComponents.garnishments.setStatus.title", { caseNumber: item.caseNumber })}
				</Modal.Heading>
			</Modal.Header>
			<Modal.Body>
				<p className="mb-4 text-sm text-muted">{t("payComponents.garnishments.setStatus.hint")}</p>
				<form
					id={formId}
					className="grid gap-4 sm:grid-cols-2"
					noValidate
					onSubmit={(event) => {
						event.preventDefault();
						void form.handleSubmit();
					}}
				>
					<form.AppField name="status">
						{(field) => (
							<field.SelectInput
								label={t("payComponents.garnishments.fields.status")}
								options={GARNISHMENT_STATUSES.map((status) => ({
									id: status,
									label: t(`payComponents.garnishments.status.${status}`),
								}))}
								isRequired
							/>
						)}
					</form.AppField>
					<form.Subscribe selector={(state) => state.values.status}>
						{(status) =>
							status === "active" ? null : (
								<form.AppField name="statusDate">
									{(field) => (
										<field.DateInput
											label={t("payComponents.garnishments.fields.statusDate")}
											isRequired
										/>
									)}
								</form.AppField>
							)
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
