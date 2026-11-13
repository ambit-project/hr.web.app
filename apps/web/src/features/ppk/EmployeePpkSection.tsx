import { AlertDialog, Button, Modal, Spinner, toast } from "@heroui/react";
import { useQuery } from "@tanstack/react-query";
import { forwardRef, useId, useImperativeHandle, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { z } from "zod";
import type { GetEmployeePpkDeclaration, GetEmployeePpkResponse } from "#/api/models";
import { AddButton } from "#/components/ui/AddButton";
import { ConfirmDelete } from "#/components/ui/ConfirmDelete";
import { Notice } from "#/features/time-absence/Notice";
import { useAppForm } from "#/forms";
import { onActionError, onSaveError, problemMessage } from "#/forms/feedback";
import { applyFieldErrors } from "#/forms/serverErrors";
import { EnrollDialog, type EnrollDialogHandle } from "./EnrollDialog";
import { DeclarationsTable, declarationKindLabel, PpkSummary, RatePeriodsTable } from "./PpkView";
import {
	employeePpkQuery,
	useCreatePpkDeclaration,
	useDeletePpkDeclaration,
	useDeletePpkParticipation,
} from "./ppkQueries";
import {
	DECLARATION_KINDS,
	type DeclarationKind,
	kindHasRate,
	parseRate,
	RATE_LIMITS,
	rateWithin,
	todayIso,
} from "./types";

/** "PPK" tab of the employee record (Kadry, Administrator): participation, rates and declarations (R6–R11). */
export function EmployeePpkSection({ employeeId, name }: { employeeId: string; name: string }) {
	const { t } = useTranslation();
	const enrollDialog = useRef<EnrollDialogHandle>(null);
	const declarationDialog = useRef<DeclarationDialogHandle>(null);
	const { data, isPending, error } = useQuery(employeePpkQuery(employeeId));

	if (isPending) return <Spinner aria-label={t("common.loading")} />;
	if (error || !data) return <Notice status="danger">{problemMessage(t, error)}</Notice>;

	return (
		<div className="flex flex-col gap-4">
			<p className="max-w-3xl text-sm text-muted">{t("ppk.participation.hint")}</p>
			<PpkSummary ppk={data} />
			<div className="flex flex-wrap gap-2">
				{data.enrolled ? (
					<DeleteParticipation ppk={data} />
				) : (
					<Button
						variant="secondary"
						onPress={() => enrollDialog.current?.enroll({ employeeId, name })}
					>
						{t("ppk.participation.enroll")}
					</Button>
				)}
			</div>
			{data.enrolled ? (
				<>
					<h3 className="text-base font-semibold">{t("ppk.participation.rateHistory")}</h3>
					<RatePeriodsTable items={data.ratePeriods} />
				</>
			) : (
				<p className="text-sm text-muted">{t("ppk.participation.notEnrolled")}</p>
			)}
			<div className="flex items-center gap-3">
				<h3 className="text-base font-semibold">{t("ppk.declarations.title")}</h3>
				<AddButton
					size="sm"
					label={t("ppk.declarations.add.title")}
					onPress={() => declarationDialog.current?.create(employeeId)}
				/>
			</div>
			<DeclarationsTable
				items={data.declarations}
				actions={(item) =>
					// The cycle's restart is undone only by a later resignation, never deleted by hand.
					item.kind === "autoRestart" ? null : <DeleteDeclaration item={item} />
				}
			/>
			<EnrollDialog ref={enrollDialog} />
			<DeclarationDialog ref={declarationDialog} />
		</div>
	);
}

function DeleteParticipation({ ppk }: { ppk: GetEmployeePpkResponse }) {
	const { t } = useTranslation();
	const remove = useDeletePpkParticipation();

	const onDelete = async () => {
		if (ppk.rowVersion === null) return;
		try {
			await remove.mutateAsync({ id: ppk.employeeId, rowVersion: ppk.rowVersion });
			toast.success(t("ppk.participation.delete.deleted"));
		} catch (error) {
			onActionError(t, error);
		}
	};

	return (
		<AlertDialog>
			<Button variant="danger-soft" isPending={remove.isPending}>
				{t("ppk.participation.delete.action")}
			</Button>
			<AlertDialog.Backdrop isDismissable={false} isKeyboardDismissDisabled>
				<AlertDialog.Container>
					<AlertDialog.Dialog className="sm:max-w-[480px]">
						<AlertDialog.Header>
							<AlertDialog.Icon status="danger" />
							<AlertDialog.Heading>{t("ppk.participation.delete.title")}</AlertDialog.Heading>
						</AlertDialog.Header>
						<AlertDialog.Body>
							<p>{t("ppk.participation.delete.body")}</p>
						</AlertDialog.Body>
						<AlertDialog.Footer>
							<Button slot="close" variant="tertiary">
								{t("common.cancel")}
							</Button>
							<Button slot="close" variant="danger" onPress={() => void onDelete()}>
								{t("ppk.participation.delete.action")}
							</Button>
						</AlertDialog.Footer>
					</AlertDialog.Dialog>
				</AlertDialog.Container>
			</AlertDialog.Backdrop>
		</AlertDialog>
	);
}

function DeleteDeclaration({ item }: { item: GetEmployeePpkDeclaration }) {
	const { t } = useTranslation();
	const remove = useDeletePpkDeclaration();

	const onDelete = async () => {
		try {
			await remove.mutateAsync({ id: item.id, rowVersion: item.rowVersion });
			toast.success(t("ppk.declarations.delete.deleted"));
		} catch (error) {
			onActionError(t, error);
		}
	};

	return (
		<ConfirmDelete
			label={t("ppk.declarations.delete.action")}
			title={t("ppk.declarations.delete.title")}
			body={t("ppk.declarations.delete.body", {
				kind: declarationKindLabel(t, item),
				date: item.submittedAt,
			})}
			onConfirm={() => void onDelete()}
		/>
	);
}

type DeclarationDialogHandle = { create: (employeeId: string) => void };

const DeclarationDialog = forwardRef<DeclarationDialogHandle>(
	function DeclarationDialog(_props, ref) {
		const [employeeId, setEmployeeId] = useState<string | null>(null);
		useImperativeHandle(ref, () => ({ create: setEmployeeId }), []);
		const close = () => setEmployeeId(null);

		return (
			<Modal>
				<Modal.Backdrop
					isDismissable={false}
					isKeyboardDismissDisabled
					isOpen={employeeId !== null}
					onOpenChange={(open) => !open && close()}
				>
					<Modal.Container scroll="inside">
						{employeeId ? <DeclarationForm employeeId={employeeId} onDone={close} /> : null}
					</Modal.Container>
				</Modal.Backdrop>
			</Modal>
		);
	},
);

// ppk_app.declaration_create: submitted not in the future, rate only for rate declarations (0.5–2 / 0–2 %).
const declarationSchema = z
	.object({
		kind: z.enum(DECLARATION_KINDS),
		submittedAt: z
			.string()
			.min(1, "required")
			.refine((value) => value <= todayIso(), "future"),
		rate: z.string(),
	})
	.superRefine((value, ctx) => {
		if (!kindHasRate(value.kind)) return;
		const { min, max } = RATE_LIMITS[value.kind];
		if (value.rate.trim() === "") {
			ctx.addIssue({ code: "custom", path: ["rate"], message: "required" });
		} else if (!rateWithin(value.rate, min, max)) {
			ctx.addIssue({ code: "custom", path: ["rate"], message: "invalid" });
		}
	});

const DECLARATION_FIELDS = ["kind", "submittedAt", "rate"] as const;

function DeclarationForm({ employeeId, onDone }: { employeeId: string; onDone: () => void }) {
	const { t } = useTranslation();
	const formId = useId();
	const create = useCreatePpkDeclaration();

	const form = useAppForm({
		defaultValues: {
			kind: "resignation" as DeclarationKind,
			submittedAt: todayIso(),
			rate: "",
		},
		validators: { onChange: declarationSchema },
		onSubmit: async ({ value, formApi }) => {
			try {
				await create.mutateAsync({
					employeeId,
					request: {
						kind: value.kind,
						submittedAt: value.submittedAt,
						rate: kindHasRate(value.kind) ? parseRate(value.rate) : null,
					},
				});
				toast.success(t("ppk.declarations.add.saved"));
				onDone();
			} catch (error) {
				onSaveError(
					t,
					error,
					(errors) => applyFieldErrors(formApi, errors, DECLARATION_FIELDS),
					onDone,
				);
			}
		},
	});

	return (
		<Modal.Dialog className="form-dialog">
			<Modal.Header>
				<Modal.Heading>{t("ppk.declarations.add.title")}</Modal.Heading>
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
					<div className="sm:col-span-2">
						<form.AppField name="kind">
							{(field) => (
								<field.SelectInput
									label={t("ppk.declarations.kind")}
									options={DECLARATION_KINDS.map((kind) => ({
										id: kind,
										label: t(`ppk.declarations.kinds.${kind}`),
									}))}
									isRequired
								/>
							)}
						</form.AppField>
					</div>
					<form.Subscribe selector={(state) => state.values.kind}>
						{(kind) => (
							<>
								<p className="text-sm text-muted sm:col-span-2">
									{t(`ppk.declarations.kindHints.${kind}`)}
								</p>
								<form.AppField name="submittedAt">
									{(field) => (
										<field.DateInput label={t("ppk.declarations.submittedAt")} isRequired />
									)}
								</form.AppField>
								{kindHasRate(kind) ? (
									<form.AppField name="rate">
										{(field) => (
											<field.TextInput label={`${t("ppk.declarations.rate")} (%)`} isRequired />
										)}
									</form.AppField>
								) : null}
							</>
						)}
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
