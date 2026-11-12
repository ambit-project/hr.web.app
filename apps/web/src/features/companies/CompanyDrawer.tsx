import { Button, Drawer, toast } from "@heroui/react";
import { forwardRef, useId, useImperativeHandle, useState } from "react";
import { useTranslation } from "react-i18next";
import { z } from "zod";
import type { ListCompaniesItem } from "#/api/models";
import { text, useAppForm } from "#/forms";
import { applyFieldErrors } from "#/forms/serverErrors";
import { ProblemError } from "#/lib/problem";
import { useCreateCompany, useUpdateCompany } from "./companiesQueries";

export type CompanyDrawerHandle = {
	create: () => void;
	edit: (company: ListCompaniesItem) => void;
};

// Mirrors the database checks that need no query (lengths, formats); checksums and duplicates stay in the database.
const schema = z.object({
	shortName: text({ required: true, max: 50 }),
	name: text({ required: true, max: 300 }),
	nip: text({ required: true, max: 20 }),
	regon: text({ required: true, max: 20 }),
	street: text({ required: true, max: 200 }),
	postalCode: text({ required: true, max: 6 }).regex(/^\d{2}-\d{3}$/, "invalid"),
	city: text({ required: true, max: 100 }),
	isActive: z.boolean(),
});

type Values = z.infer<typeof schema>;
const FIELDS = ["shortName", "name", "nip", "regon", "street", "postalCode", "city"] as const;

const EMPTY: Values = {
	shortName: "",
	name: "",
	nip: "",
	regon: "",
	street: "",
	postalCode: "",
	city: "",
	isActive: true,
};

type Target = { mode: "create" } | { mode: "edit"; company: ListCompaniesItem };

export const CompanyDrawer = forwardRef<CompanyDrawerHandle>(function CompanyDrawer(_props, ref) {
	const [target, setTarget] = useState<Target | null>(null);
	useImperativeHandle(
		ref,
		() => ({
			create: () => setTarget({ mode: "create" }),
			edit: (company) => setTarget({ mode: "edit", company }),
		}),
		[],
	);

	return (
		<Drawer>
			<Drawer.Backdrop isOpen={target !== null} onOpenChange={(open) => !open && setTarget(null)}>
				<Drawer.Content placement="right">
					{target ? <CompanyForm target={target} onDone={() => setTarget(null)} /> : null}
				</Drawer.Content>
			</Drawer.Backdrop>
		</Drawer>
	);
});

function CompanyForm({ target, onDone }: { target: Target; onDone: () => void }) {
	const { t } = useTranslation();
	const formId = useId();
	const create = useCreateCompany();
	const update = useUpdateCompany();

	const form = useAppForm({
		defaultValues: target.mode === "edit" ? { ...target.company } : EMPTY,
		validators: { onChange: schema },
		onSubmit: async ({ value, formApi }) => {
			const fields = {
				shortName: value.shortName,
				name: value.name,
				nip: value.nip,
				regon: value.regon,
				street: value.street,
				postalCode: value.postalCode,
				city: value.city,
			};
			try {
				if (target.mode === "create") {
					await create.mutateAsync(fields);
					toast.success(t("companies.create.created", { name: value.shortName.trim() }));
				} else {
					await update.mutateAsync({
						id: target.company.id,
						request: { ...fields, isActive: value.isActive, rowVersion: target.company.rowVersion },
					});
					toast.success(t("companies.edit.saved", { name: value.shortName.trim() }));
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
					{target.mode === "create" ? t("companies.create.title") : t("companies.edit.title")}
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
					{FIELDS.map((name) => (
						<form.AppField key={name} name={name}>
							{(field) => <field.TextInput label={t(`companies.fields.${name}`)} isRequired />}
						</form.AppField>
					))}
					{target.mode === "edit" ? (
						<form.AppField name="isActive">
							{(field) => <field.SwitchInput label={t("companies.fields.isActive")} />}
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
