import { Button, Drawer, toast } from "@heroui/react";
import { forwardRef, useId, useImperativeHandle, useState } from "react";
import { useTranslation } from "react-i18next";
import { z } from "zod";
import type { ListTaxDeclarationsItem } from "#/api/models";
import { useAppForm } from "#/forms";
import { applyFieldErrors } from "#/forms/serverErrors";
import { ProblemError } from "#/lib/problem";
import { useCreateTaxDeclaration, useEndTaxDeclaration } from "./taxDeclarationsQueries";
import {
	allowedMonths,
	DECLARATION_KINDS,
	type DeclarationKind,
	formatMonth,
	PIT2_SHARES,
} from "./types";

export type TaxDeclarationDrawerHandle = {
	create: (employeeId: string) => void;
	end: (item: ListTaxDeclarationsItem) => void;
};

type Target =
	| { mode: "create"; employeeId: string }
	| { mode: "end"; item: ListTaxDeclarationsItem };

export const TaxDeclarationDrawer = forwardRef<TaxDeclarationDrawerHandle>(
	function TaxDeclarationDrawer(_props, ref) {
		const [target, setTarget] = useState<Target | null>(null);
		useImperativeHandle(
			ref,
			() => ({
				create: (employeeId) => setTarget({ mode: "create", employeeId }),
				end: (item) => setTarget({ mode: "end", item }),
			}),
			[],
		);
		const close = () => setTarget(null);

		return (
			<Drawer>
				<Drawer.Backdrop isOpen={target !== null} onOpenChange={(open) => !open && close()}>
					<Drawer.Content placement="right">
						{target?.mode === "create" ? (
							<CreateForm employeeId={target.employeeId} onDone={close} />
						) : target?.mode === "end" ? (
							<EndForm item={target.item} onDone={close} />
						) : null}
					</Drawer.Content>
				</Drawer.Backdrop>
			</Drawer>
		);
	},
);

function onSaveError(
	t: ReturnType<typeof useTranslation>["t"],
	error: unknown,
	applyErrors: (errors: Record<string, string[]> | undefined) => boolean,
	onDone: () => void,
) {
	if (error instanceof ProblemError && error.problem.status === 409) {
		toast.warning(t("common.conflict"));
		onDone();
	} else if (!(error instanceof ProblemError) || !applyErrors(error.problem.errors)) {
		toast.danger(t("common.serverError"));
	}
}

const CREATE_FIELDS = ["kind", "share", "yearFrom", "yearTo", "submittedAt", "validFrom"] as const;

const createSchema = z.object({
	kind: z.enum(DECLARATION_KINDS),
	share: z.string(),
	yearFrom: z.string().regex(/^\s*(\d{4})?\s*$/, "invalid"),
	yearTo: z.string().regex(/^\s*(\d{4})?\s*$/, "invalid"),
	submittedAt: z.string(),
	validFrom: z.string(),
});

function CreateForm({ employeeId, onDone }: { employeeId: string; onDone: () => void }) {
	const { t } = useTranslation();
	const formId = useId();
	const create = useCreateTaxDeclaration();

	const form = useAppForm({
		defaultValues: {
			kind: "pit2" as DeclarationKind,
			share: "1/12",
			yearFrom: "",
			yearTo: "",
			submittedAt: "",
			validFrom: "",
		},
		validators: { onChange: createSchema },
		onSubmit: async ({ value, formApi }) => {
			const blank = (text: string) => (text.trim() === "" ? null : text.trim());
			const year = (text: string) => (text.trim() === "" ? null : Number(text));
			const relief = value.kind === "returnRelief";
			try {
				await create.mutateAsync({
					employeeId,
					request: {
						kind: value.kind,
						share: value.kind === "pit2" ? blank(value.share) : null,
						yearFrom: relief ? year(value.yearFrom) : null,
						yearTo: relief ? year(value.yearTo) : null,
						submittedAt: blank(value.submittedAt),
						validFrom: blank(value.validFrom),
						validTo: null,
					},
				});
				toast.success(t("taxDeclarations.create.created"));
				onDone();
			} catch (error) {
				onSaveError(t, error, (errors) => applyFieldErrors(formApi, errors, CREATE_FIELDS), onDone);
			}
		},
	});

	return (
		<Drawer.Dialog>
			<Drawer.Header>
				<Drawer.Heading>{t("taxDeclarations.create.title")}</Drawer.Heading>
			</Drawer.Header>
			<Drawer.Body>
				<form
					id={formId}
					className="flex flex-col gap-4"
					noValidate
					onSubmit={(event) => {
						event.preventDefault();
						void form.handleSubmit();
					}}
				>
					<form.AppField name="kind">
						{(field) => (
							<field.SelectInput
								label={t("taxDeclarations.fields.kind")}
								options={DECLARATION_KINDS.map((kind) => ({
									id: kind,
									label: t(`taxDeclarations.kinds.${kind}`),
								}))}
								isRequired
							/>
						)}
					</form.AppField>
					<form.Subscribe selector={(state) => state.values.kind}>
						{(kind) => (
							<>
								<p className="text-sm text-muted">{t(`taxDeclarations.basis.${kind}`)}</p>
								{kind === "pit2" ? (
									<form.AppField name="share">
										{(field) => (
											<field.SelectInput
												label={t("taxDeclarations.fields.share")}
												options={PIT2_SHARES.map((share) => ({
													id: share,
													label: t("taxDeclarations.shareValue", { share }),
												}))}
												isRequired
											/>
										)}
									</form.AppField>
								) : null}
								{kind === "returnRelief" ? (
									<div className="grid grid-cols-2 gap-3">
										<form.AppField name="yearFrom">
											{(field) => (
												<field.TextInput label={t("taxDeclarations.fields.yearFrom")} isRequired />
											)}
										</form.AppField>
										<form.AppField name="yearTo">
											{(field) => (
												<field.TextInput label={t("taxDeclarations.fields.yearTo")} isRequired />
											)}
										</form.AppField>
									</div>
								) : null}
							</>
						)}
					</form.Subscribe>
					<form.AppField
						name="submittedAt"
						listeners={{
							// Payers apply a declaration from the next month at the latest, so that is the default.
							onChange: ({ value }) =>
								form.setFieldValue("validFrom", allowedMonths(value)[1] ?? ""),
						}}
					>
						{(field) => (
							<field.DateInput label={t("taxDeclarations.fields.submittedAt")} isRequired />
						)}
					</form.AppField>
					<form.Subscribe selector={(state) => state.values.submittedAt}>
						{(submittedAt) => (
							<form.AppField name="validFrom">
								{(field) => (
									<field.SelectInput
										label={t("taxDeclarations.fields.validFrom")}
										options={allowedMonths(submittedAt).map((month) => ({
											id: month,
											label: formatMonth(month),
										}))}
										isRequired
									/>
								)}
							</form.AppField>
						)}
					</form.Subscribe>
					<p className="text-sm text-muted">{t("taxDeclarations.validFromHint")}</p>
				</form>
			</Drawer.Body>
			<Drawer.Footer>
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
			</Drawer.Footer>
		</Drawer.Dialog>
	);
}

const endSchema = z.object({ withdrawnAt: z.string(), stopFrom: z.string() });

function EndForm({ item, onDone }: { item: ListTaxDeclarationsItem; onDone: () => void }) {
	const { t } = useTranslation();
	const formId = useId();
	const end = useEndTaxDeclaration();

	const save = async (
		withdrawnAt: string | null,
		stopFrom: string | null,
		applyErrors: (errors: Record<string, string[]> | undefined) => boolean,
	) => {
		try {
			await end.mutateAsync({
				id: item.id,
				request: { rowVersion: item.rowVersion, withdrawnAt, stopFrom },
			});
			toast.success(stopFrom ? t("taxDeclarations.end.saved") : t("taxDeclarations.end.undone"));
			onDone();
		} catch (error) {
			onSaveError(t, error, applyErrors, onDone);
		}
	};

	const form = useAppForm({
		defaultValues: {
			withdrawnAt: item.withdrawnAt ?? "",
			stopFrom: item.withdrawnAt && item.validTo ? nextDay(item.validTo) : "",
		},
		// Shape only; it also lets a later edit clear the server's field errors (see applyFieldErrors).
		validators: { onChange: endSchema },
		onSubmit: async ({ value, formApi }) =>
			save(value.withdrawnAt || null, value.stopFrom || null, (errors) =>
				applyFieldErrors(formApi, errors, ["withdrawnAt", "stopFrom"] as const),
			),
	});

	return (
		<Drawer.Dialog>
			<Drawer.Header>
				<Drawer.Heading>
					{t("taxDeclarations.end.title", {
						kind: t(`taxDeclarations.kinds.${item.kind as DeclarationKind}`),
					})}
				</Drawer.Heading>
			</Drawer.Header>
			<Drawer.Body>
				<p className="mb-4 text-sm text-muted">{t("taxDeclarations.end.hint")}</p>
				<form
					id={formId}
					className="flex flex-col gap-4"
					noValidate
					onSubmit={(event) => {
						event.preventDefault();
						void form.handleSubmit();
					}}
				>
					<form.AppField
						name="withdrawnAt"
						listeners={{
							onChange: ({ value }) =>
								form.setFieldValue("stopFrom", allowedMonths(value)[1] ?? ""),
						}}
					>
						{(field) => (
							<field.DateInput label={t("taxDeclarations.fields.withdrawnAt")} isRequired />
						)}
					</form.AppField>
					<form.Subscribe selector={(state) => state.values.withdrawnAt}>
						{(withdrawnAt) => (
							<form.AppField name="stopFrom">
								{(field) => (
									<field.SelectInput
										label={t("taxDeclarations.fields.stopFrom")}
										options={allowedMonths(withdrawnAt).map((month) => ({
											id: month,
											label: formatMonth(month),
										}))}
										isRequired
									/>
								)}
							</form.AppField>
						)}
					</form.Subscribe>
				</form>
				{item.withdrawnAt ? (
					<Button
						className="mt-4"
						variant="tertiary"
						onPress={() =>
							void save(null, null, (errors) => {
								const message = errors?.stopFrom?.[0];
								if (message) toast.danger(t(`validation.${message}`, t("validation.unknown")));
								return Boolean(message);
							})
						}
					>
						{t("taxDeclarations.end.undo")}
					</Button>
				) : null}
			</Drawer.Body>
			<Drawer.Footer>
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
			</Drawer.Footer>
		</Drawer.Dialog>
	);
}

/** "2026-06-30" → "2026-07-01" (the month the entry stopped applying from). */
function nextDay(date: string): string {
	const value = new Date(`${date}T00:00:00Z`);
	value.setUTCDate(value.getUTCDate() + 1);
	return value.toISOString().slice(0, 10);
}
