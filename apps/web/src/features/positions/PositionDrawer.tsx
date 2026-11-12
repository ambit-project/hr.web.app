import { Button, Drawer, toast } from "@heroui/react";
import { forwardRef, useId, useImperativeHandle, useState } from "react";
import { useTranslation } from "react-i18next";
import { z } from "zod";
import type { ListPositionsItem } from "#/api/models";
import { text, useAppForm } from "#/forms";
import { applyFieldErrors } from "#/forms/serverErrors";
import { ProblemError } from "#/lib/problem";
import { useCreatePosition, useUpdatePosition } from "./positionsQueries";

export type PositionDrawerHandle = {
	create: () => void;
	edit: (position: ListPositionsItem) => void;
};

type Target = { mode: "create" } | { mode: "edit"; position: ListPositionsItem };

const schema = z.object({
	name: text({ required: true, max: 200 }),
	description: text({ max: 1000 }),
	isActive: z.boolean(),
});

const FIELDS = ["name", "description"] as const;

export const PositionDrawer = forwardRef<PositionDrawerHandle>(
	function PositionDrawer(_props, ref) {
		const [target, setTarget] = useState<Target | null>(null);
		useImperativeHandle(
			ref,
			() => ({
				create: () => setTarget({ mode: "create" }),
				edit: (position) => setTarget({ mode: "edit", position }),
			}),
			[],
		);

		return (
			<Drawer>
				<Drawer.Backdrop isOpen={target !== null} onOpenChange={(open) => !open && setTarget(null)}>
					<Drawer.Content placement="right">
						{target ? <PositionForm target={target} onDone={() => setTarget(null)} /> : null}
					</Drawer.Content>
				</Drawer.Backdrop>
			</Drawer>
		);
	},
);

function PositionForm({ target, onDone }: { target: Target; onDone: () => void }) {
	const { t } = useTranslation();
	const formId = useId();
	const create = useCreatePosition();
	const update = useUpdatePosition();
	const position = target.mode === "edit" ? target.position : null;

	const form = useAppForm({
		defaultValues: {
			name: position?.name ?? "",
			description: position?.description ?? "",
			isActive: position?.isActive ?? true,
		},
		validators: { onChange: schema },
		onSubmit: async ({ value, formApi }) => {
			const description = value.description.trim() === "" ? null : value.description;
			try {
				if (position) {
					await update.mutateAsync({
						id: position.id,
						request: {
							rowVersion: position.rowVersion,
							name: value.name,
							description,
							isActive: value.isActive,
						},
					});
					toast.success(t("positions.edit.saved", { name: value.name.trim() }));
				} else {
					await create.mutateAsync({ name: value.name, description });
					toast.success(t("positions.create.created", { name: value.name.trim() }));
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
		<Drawer.Dialog>
			<Drawer.Header>
				<Drawer.Heading>
					{position ? t("positions.edit.title") : t("positions.create.title")}
				</Drawer.Heading>
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
					<form.AppField name="name">
						{(field) => <field.TextInput label={t("positions.fields.name")} isRequired />}
					</form.AppField>
					<form.AppField name="description">
						{(field) => <field.TextInput label={t("positions.fields.description")} multiline />}
					</form.AppField>
					{position ? (
						<form.AppField name="isActive">
							{(field) => <field.SwitchInput label={t("positions.fields.isActive")} />}
						</form.AppField>
					) : null}
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
