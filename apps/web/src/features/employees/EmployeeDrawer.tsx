import { Button, Drawer, toast } from "@heroui/react";
import { useNavigate } from "@tanstack/react-router";
import { forwardRef, useId, useImperativeHandle, useState } from "react";
import { useTranslation } from "react-i18next";
import { z } from "zod";
import type { GetEmployeeResponse } from "#/api/models";
import { CompanySelect } from "#/features/companies/CompanySelect";
import { TaxOfficePicker } from "#/features/tax-offices/TaxOfficePicker";
import { text, useAppForm } from "#/forms";
import { errorCode } from "#/forms/errors";
import { applyFieldErrors } from "#/forms/serverErrors";
import { ProblemError } from "#/lib/problem";
import { useCreateEmployee, useUpdateEmployee } from "./employeesQueries";
import { peselBirthDate } from "./pesel";

export type EmployeeDrawerHandle = {
	create: (companyId?: string) => void;
	edit: (employee: GetEmployeeResponse) => void;
};

type Target =
	| { mode: "create"; companyId: string }
	| { mode: "edit"; employee: GetEmployeeResponse };

// Mirrors the checks that need no query; PESEL checksum, uniqueness and the tax office stay in the database.
const schema = z
	.object({
		companyId: z.string().min(1, "required"),
		number: text({ max: 20 }),
		firstName: text({ required: true, max: 100 }),
		secondName: text({ max: 100 }),
		lastName: text({ required: true, max: 100 }),
		pesel: text({ max: 20 }),
		identityDocument: text({ max: 100 }),
		birthDate: text({ required: true, max: 10 }),
		taxOfficeId: z.string().min(1, "required"),
		email: text({ max: 200 }),
		street: text({ max: 200 }),
		postalCode: text({ max: 6 }),
		city: text({ max: 100 }),
		addressFrom: z.string(),
		withAddress: z.boolean(),
	})
	.superRefine((value, ctx) => {
		const pesel = value.pesel.trim();
		if (pesel === "" && value.identityDocument.trim() === "") {
			ctx.addIssue({ code: "custom", path: ["pesel"], message: "required" });
		} else if (pesel !== "" && peselBirthDate(pesel) === null) {
			ctx.addIssue({ code: "custom", path: ["pesel"], message: "invalid" });
		}
		if (!value.withAddress) return;
		for (const field of ["street", "city", "addressFrom"] as const) {
			if (value[field].trim() === "") {
				ctx.addIssue({ code: "custom", path: [field], message: "required" });
			}
		}
		if (!/^\d{2}-\d{3}$/.test(value.postalCode.trim())) {
			ctx.addIssue({
				code: "custom",
				path: ["postalCode"],
				message: value.postalCode.trim() === "" ? "required" : "invalid",
			});
		}
	});

const FIELDS = [
	"companyId",
	"number",
	"firstName",
	"secondName",
	"lastName",
	"pesel",
	"identityDocument",
	"birthDate",
	"taxOfficeId",
	"email",
	"street",
	"postalCode",
	"city",
	"addressFrom",
] as const;

const PERSON_FIELDS = ["number", "firstName", "secondName", "lastName"] as const;

export const EmployeeDrawer = forwardRef<EmployeeDrawerHandle>(
	function EmployeeDrawer(_props, ref) {
		const [target, setTarget] = useState<Target | null>(null);
		useImperativeHandle(
			ref,
			() => ({
				create: (companyId) => setTarget({ mode: "create", companyId: companyId ?? "" }),
				edit: (employee) => setTarget({ mode: "edit", employee }),
			}),
			[],
		);

		return (
			<Drawer>
				<Drawer.Backdrop isOpen={target !== null} onOpenChange={(open) => !open && setTarget(null)}>
					<Drawer.Content placement="right">
						{target ? <EmployeeForm target={target} onDone={() => setTarget(null)} /> : null}
					</Drawer.Content>
				</Drawer.Backdrop>
			</Drawer>
		);
	},
);

function EmployeeForm({ target, onDone }: { target: Target; onDone: () => void }) {
	const { t } = useTranslation();
	const formId = useId();
	const navigate = useNavigate();
	const create = useCreateEmployee();
	const update = useUpdateEmployee();
	const employee = target.mode === "edit" ? target.employee : null;

	const form = useAppForm({
		defaultValues: {
			companyId: employee?.companyId ?? (target.mode === "create" ? target.companyId : ""),
			number: employee?.number ?? "",
			firstName: employee?.firstName ?? "",
			secondName: employee?.secondName ?? "",
			lastName: employee?.lastName ?? "",
			pesel: employee?.pesel ?? "",
			identityDocument: employee?.identityDocument ?? "",
			birthDate: employee?.birthDate ?? "",
			taxOfficeId: employee?.taxOfficeId ?? "",
			email: employee?.email ?? "",
			street: "",
			postalCode: "",
			city: "",
			addressFrom: "",
			withAddress: employee === null,
		},
		validators: { onChange: schema },
		onSubmit: async ({ value, formApi }) => {
			const optional = (input: string) => (input.trim() === "" ? null : input);
			const person = {
				firstName: value.firstName,
				secondName: optional(value.secondName),
				lastName: value.lastName,
				pesel: optional(value.pesel),
				identityDocument: optional(value.identityDocument),
				birthDate: value.birthDate,
				taxOfficeId: value.taxOfficeId,
				email: optional(value.email),
			};
			try {
				if (employee) {
					await update.mutateAsync({
						id: employee.id,
						request: { ...person, number: value.number, rowVersion: employee.rowVersion },
					});
					toast.success(t("employees.edit.saved"));
					onDone();
				} else {
					const { id } = await create.mutateAsync({
						...person,
						companyId: value.companyId,
						number: optional(value.number),
						street: value.street,
						postalCode: value.postalCode,
						city: value.city,
						addressFrom: value.addressFrom,
					});
					toast.success(t("employees.create.created"));
					onDone();
					await navigate({ to: "/employees/$id", params: { id }, search: { tab: undefined } });
				}
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
					{employee ? t("employees.edit.title") : t("employees.create.title")}
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
					{employee ? (
						<p className="text-sm">
							{t("employees.fields.company")}: <strong>{employee.companyShortName}</strong>
						</p>
					) : (
						<form.Field name="companyId">
							{(field) => {
								const code = errorCode(field.state.meta.errors);
								return (
									<CompanySelect
										label={t("employees.fields.company")}
										value={field.state.value}
										onChange={field.handleChange}
										isRequired
										errorMessage={
											code && field.state.meta.isTouched ? t(`validation.${code}`) : null
										}
									/>
								);
							}}
						</form.Field>
					)}
					{PERSON_FIELDS.map((name) => (
						<form.AppField key={name} name={name}>
							{(field) => (
								<field.TextInput
									label={t(`employees.fields.${name}`)}
									isRequired={name === "firstName" || name === "lastName"}
									description={
										name === "number" && !employee ? t("employees.fields.numberHint") : undefined
									}
								/>
							)}
						</form.AppField>
					))}
					<form.AppField
						name="pesel"
						listeners={{
							onChange: ({ value }) => {
								const birthDate = peselBirthDate(value);
								if (birthDate && form.getFieldValue("birthDate") === "") {
									form.setFieldValue("birthDate", birthDate);
								}
							},
						}}
					>
						{(field) => <field.TextInput label={t("employees.fields.pesel")} />}
					</form.AppField>
					<form.AppField name="identityDocument">
						{(field) => (
							<field.TextInput
								label={t("employees.fields.identityDocument")}
								description={t("employees.fields.identityDocumentHint")}
							/>
						)}
					</form.AppField>
					<form.AppField name="birthDate">
						{(field) => <field.DateInput label={t("employees.fields.birthDate")} isRequired />}
					</form.AppField>
					<form.Field name="taxOfficeId">
						{(field) => {
							const code = errorCode(field.state.meta.errors);
							return (
								<TaxOfficePicker
									label={t("employees.fields.taxOffice")}
									value={field.state.value}
									initialLabel={
										employee?.taxOfficeCode
											? `${employee.taxOfficeCode} ${employee.taxOfficeName ?? ""}`.trim()
											: undefined
									}
									onChange={field.handleChange}
									onBlur={field.handleBlur}
									isRequired
									errorMessage={code && field.state.meta.isTouched ? t(`validation.${code}`) : null}
								/>
							);
						}}
					</form.Field>
					<form.AppField name="email">
						{(field) => <field.TextInput label={t("employees.fields.email")} type="email" />}
					</form.AppField>
					{employee ? null : (
						<>
							<h3 className="pt-2 text-sm font-semibold">{t("employees.addresses.residence")}</h3>
							<form.AppField name="street">
								{(field) => <field.TextInput label={t("employees.fields.street")} isRequired />}
							</form.AppField>
							<form.AppField name="postalCode">
								{(field) => <field.TextInput label={t("employees.fields.postalCode")} isRequired />}
							</form.AppField>
							<form.AppField name="city">
								{(field) => <field.TextInput label={t("employees.fields.city")} isRequired />}
							</form.AppField>
							<form.AppField name="addressFrom">
								{(field) => <field.DateInput label={t("employees.fields.validFrom")} isRequired />}
							</form.AppField>
						</>
					)}
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
