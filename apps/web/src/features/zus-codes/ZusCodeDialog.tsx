import { Button, Label, ListBox, Modal, Select, toast } from "@heroui/react";
import { forwardRef, useId, useImperativeHandle, useState } from "react";
import { useTranslation } from "react-i18next";
import { z } from "zod";
import type { ListZusCodesItem } from "#/api/models";
import { text, useAppForm } from "#/forms";
import { applyFieldErrors } from "#/forms/serverErrors";
import { ProblemError } from "#/lib/problem";
import { isZusCodeKind, ZUS_CODE_KINDS, ZUS_CODE_PATTERN, type ZusCodeKind } from "./types";
import { useCreateZusCode, useUpdateZusCode } from "./zusCodesQueries";

export type ZusCodeDialogHandle = {
	create: (kind?: ZusCodeKind) => void;
	edit: (code: ListZusCodesItem) => void;
};

type Target = { mode: "create"; kind: ZusCodeKind } | { mode: "edit"; code: ListZusCodesItem };

// Mirrors the database checks that need no query; uniqueness stays in the database.
const schema = z
	.object({
		kind: z.enum(ZUS_CODE_KINDS),
		code: text({ required: true, max: 4 }),
		name: text({ required: true, max: 2000 }),
		validFrom: text({ required: true, max: 10 }),
		validTo: z.string(),
	})
	.superRefine((value, ctx) => {
		if (value.code.trim() !== "" && !ZUS_CODE_PATTERN[value.kind].test(value.code.trim())) {
			ctx.addIssue({ code: "custom", path: ["code"], message: "invalid" });
		}
		if (value.validTo !== "" && value.validFrom !== "" && value.validTo < value.validFrom) {
			ctx.addIssue({ code: "custom", path: ["validTo"], message: "beforeFrom" });
		}
	});

const FIELDS = ["kind", "code", "name", "validFrom", "validTo"] as const;

export const ZusCodeDialog = forwardRef<ZusCodeDialogHandle>(function ZusCodeDialog(_props, ref) {
	const [target, setTarget] = useState<Target | null>(null);
	useImperativeHandle(
		ref,
		() => ({
			create: (kind) => setTarget({ mode: "create", kind: kind ?? "title" }),
			edit: (code) => setTarget({ mode: "edit", code }),
		}),
		[],
	);

	return (
		<Modal>
			<Modal.Backdrop
				isDismissable={false}
				isKeyboardDismissDisabled
				isOpen={target !== null}
				onOpenChange={(open) => !open && setTarget(null)}
			>
				<Modal.Container scroll="inside">
					{target ? <ZusCodeForm target={target} onDone={() => setTarget(null)} /> : null}
				</Modal.Container>
			</Modal.Backdrop>
		</Modal>
	);
});

function ZusCodeForm({ target, onDone }: { target: Target; onDone: () => void }) {
	const { t } = useTranslation();
	const formId = useId();
	const create = useCreateZusCode();
	const update = useUpdateZusCode();
	const existing = target.mode === "edit" ? target.code : null;
	const kind: ZusCodeKind =
		target.mode === "create"
			? target.kind
			: isZusCodeKind(target.code.kind)
				? target.code.kind
				: "title";

	const form = useAppForm({
		defaultValues: {
			kind,
			code: existing?.code ?? "",
			name: existing?.name ?? "",
			validFrom: existing?.validFrom ?? "",
			validTo: existing?.validTo ?? "",
		},
		validators: { onChange: schema },
		onSubmit: async ({ value, formApi }) => {
			const period = {
				name: value.name,
				validFrom: value.validFrom,
				validTo: value.validTo === "" ? null : value.validTo,
			};
			try {
				if (existing) {
					await update.mutateAsync({
						id: existing.id,
						request: { ...period, rowVersion: existing.rowVersion },
					});
					toast.success(t("zusCodes.edit.saved", { code: existing.code }));
				} else {
					await create.mutateAsync({ ...period, kind: value.kind, code: value.code.trim() });
					toast.success(t("zusCodes.create.created", { code: value.code.trim() }));
				}
				onDone();
			} catch (error) {
				if (error instanceof ProblemError && error.problem.status === 409) {
					toast.warning(t("common.conflict"));
					onDone();
				} else if (
					!(error instanceof ProblemError) ||
					!applyFieldErrors(formApi, error.problem.errors, FIELDS)
				) {
					toast.danger(t("common.serverError"));
				}
			}
		},
	});

	return (
		<Modal.Dialog className="form-dialog">
			<Modal.Header>
				<Modal.Heading>
					{existing
						? t("zusCodes.edit.title", { code: existing.code })
						: t("zusCodes.create.title")}
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
					{existing ? (
						<p className="text-sm">
							{t(`zusCodes.kinds.${existing.kind as ZusCodeKind}`)}: <code>{existing.code}</code>
						</p>
					) : (
						<>
							<form.Field name="kind">
								{(field) => (
									<Select
										value={field.state.value}
										onChange={(key) => key !== null && field.handleChange(key as ZusCodeKind)}
									>
										<Label>{t("zusCodes.kind")}</Label>
										<Select.Trigger>
											<Select.Value />
											<Select.Indicator />
										</Select.Trigger>
										<Select.Popover>
											<ListBox>
												{ZUS_CODE_KINDS.map((kind) => (
													<ListBox.Item
														key={kind}
														id={kind}
														textValue={t(`zusCodes.kinds.${kind}`)}
													>
														{t(`zusCodes.kinds.${kind}`)}
														<ListBox.ItemIndicator />
													</ListBox.Item>
												))}
											</ListBox>
										</Select.Popover>
									</Select>
								)}
							</form.Field>
							<form.AppField name="code">
								{(field) => <field.TextInput label={t("zusCodes.fields.code")} isRequired />}
							</form.AppField>
						</>
					)}
					<form.AppField name="name">
						{(field) => <field.TextInput label={t("zusCodes.fields.name")} isRequired multiline />}
					</form.AppField>
					<div className="grid gap-4 sm:grid-cols-2">
						<form.AppField name="validFrom">
							{(field) => <field.DateInput label={t("zusCodes.fields.validFrom")} isRequired />}
						</form.AppField>
						<form.AppField name="validTo">
							{(field) => <field.DateInput label={t("zusCodes.fields.validTo")} />}
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
