import { Button, Modal, toast } from "@heroui/react";
import { forwardRef, useId, useImperativeHandle, useState } from "react";
import { useTranslation } from "react-i18next";
import { z } from "zod";
import type { ListCompaniesItem } from "#/api/models";
import { TaxOfficePicker } from "#/features/tax-offices/TaxOfficePicker";
import { text, useAppForm } from "#/forms";
import { errorCode } from "#/forms/errors";
import { applyFieldErrors } from "#/forms/serverErrors";
import { ProblemError, serverErrorText } from "#/lib/problem";
import { useCreateCompany, useUpdateCompany } from "./companiesQueries";

export type CompanyDialogHandle = {
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
	taxOfficeId: z.string().min(1, "required"),
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
	taxOfficeId: "",
	isActive: true,
};

type Target = { mode: "create" } | { mode: "edit"; company: ListCompaniesItem };

export const CompanyDialog = forwardRef<CompanyDialogHandle>(function CompanyDialog(_props, ref) {
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
		<Modal>
			<Modal.Backdrop
				isDismissable={false}
				isKeyboardDismissDisabled
				isOpen={target !== null}
				onOpenChange={(open) => !open && setTarget(null)}
			>
				<Modal.Container scroll="inside">
					{target ? <CompanyForm target={target} onDone={() => setTarget(null)} /> : null}
				</Modal.Container>
			</Modal.Backdrop>
		</Modal>
	);
});

function CompanyForm({ target, onDone }: { target: Target; onDone: () => void }) {
	const { t } = useTranslation();
	const formId = useId();
	const create = useCreateCompany();
	const update = useUpdateCompany();

	const form = useAppForm({
		defaultValues: target.mode === "edit" ? toValues(target.company) : EMPTY,
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
				taxOfficeId: value.taxOfficeId,
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
					!applyFieldErrors(formApi, error.problem.errors, [...FIELDS, "taxOfficeId"] as const)
				) {
					toast.danger(serverErrorText(t));
				}
			}
		},
	});

	return (
		<Modal.Dialog className="form-dialog">
			<Modal.Header>
				<Modal.Heading>
					{target.mode === "create" ? t("companies.create.title") : t("companies.edit.title")}
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
					{/* Long texts take the whole width; short identifiers and the postal code sit in pairs. */}
					{(
						[["shortName"], ["name"], ["nip", "regon"], ["street"], ["postalCode", "city"]] as const
					).map((row) => (
						<div
							key={row.join("-")}
							className={row.length > 1 ? "grid gap-4 sm:grid-cols-2" : undefined}
						>
							{row.map((name) => (
								<form.AppField key={name} name={name}>
									{(field) => <field.TextInput label={t(`companies.fields.${name}`)} isRequired />}
								</form.AppField>
							))}
						</div>
					))}
					<form.Field name="taxOfficeId">
						{(field) => {
							const code = errorCode(field.state.meta.errors);
							return (
								<TaxOfficePicker
									label={t("companies.fields.taxOffice")}
									value={field.state.value}
									initialLabel={officeLabel(target)}
									onChange={field.handleChange}
									onBlur={field.handleBlur}
									isRequired
									errorMessage={code && field.state.meta.isTouched ? t(`validation.${code}`) : null}
								/>
							);
						}}
					</form.Field>
					{target.mode === "edit" ? (
						<form.AppField name="isActive">
							{(field) => <field.SwitchInput label={t("companies.fields.isActive")} />}
						</form.AppField>
					) : null}
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

function toValues(company: ListCompaniesItem): Values {
	return {
		shortName: company.shortName,
		name: company.name,
		nip: company.nip,
		regon: company.regon,
		street: company.street,
		postalCode: company.postalCode,
		city: company.city,
		taxOfficeId: company.taxOfficeId ?? "",
		isActive: company.isActive,
	};
}

function officeLabel(target: Target): string | undefined {
	if (target.mode !== "edit" || !target.company.taxOfficeCode) return undefined;
	return `${target.company.taxOfficeCode} ${target.company.taxOfficeName ?? ""}`.trim();
}
