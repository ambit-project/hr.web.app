import { Button, Drawer, toast } from "@heroui/react";
import { forwardRef, useId, useImperativeHandle, useState } from "react";
import { useTranslation } from "react-i18next";
import { z } from "zod";
import { text, useAppForm } from "#/forms";
import { applyFieldErrors } from "#/forms/serverErrors";
import { ProblemError } from "#/lib/problem";
import { useCreatePosition } from "./positionsQueries";

export type CreatePositionDrawerHandle = { open: () => void };

const schema = z.object({
	name: text({ required: true, max: 200 }),
	description: text({ max: 1000 }),
});

export const CreatePositionDrawer = forwardRef<CreatePositionDrawerHandle>(
	function CreatePositionDrawer(_props, ref) {
		const [isOpen, setOpen] = useState(false);
		useImperativeHandle(ref, () => ({ open: () => setOpen(true) }), []);

		return (
			<Drawer>
				<Drawer.Backdrop isOpen={isOpen} onOpenChange={setOpen}>
					<Drawer.Content placement="right">
						{isOpen ? <CreatePositionForm onDone={() => setOpen(false)} /> : null}
					</Drawer.Content>
				</Drawer.Backdrop>
			</Drawer>
		);
	},
);

function CreatePositionForm({ onDone }: { onDone: () => void }) {
	const { t } = useTranslation();
	const formId = useId();
	const create = useCreatePosition();

	const form = useAppForm({
		defaultValues: { name: "", description: "" },
		validators: { onChange: schema },
		onSubmit: async ({ value, formApi }) => {
			try {
				await create.mutateAsync({
					name: value.name,
					description: value.description.trim() === "" ? null : value.description,
				});
				toast.success(t("positions.create.created", { name: value.name.trim() }));
				onDone();
			} catch (error) {
				if (
					!(error instanceof ProblemError) ||
					!applyFieldErrors(formApi, error.problem.errors, ["name", "description"] as const)
				) {
					toast.danger(t("common.serverError"));
				}
			}
		},
	});

	return (
		<Drawer.Dialog>
			<Drawer.Header>
				<Drawer.Heading>{t("positions.create.title")}</Drawer.Heading>
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
						{(field) => <field.TextInput label={t("positions.create.name")} isRequired />}
					</form.AppField>
					<form.AppField name="description">
						{(field) => <field.TextInput label={t("positions.create.description")} multiline />}
					</form.AppField>
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
