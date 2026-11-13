import { Button, Checkbox, CheckboxGroup, Description, Label, Modal, toast } from "@heroui/react";
import { useQuery } from "@tanstack/react-query";
import { useNavigate } from "@tanstack/react-router";
import { forwardRef, useId, useImperativeHandle, useState } from "react";
import { useTranslation } from "react-i18next";
import { z } from "zod";
import type { GetEmploymentResponse, GetEmploymentTerms } from "#/api/models";
import { OptionSelect } from "#/components/ui/OptionSelect";
import { firstStepWithError, WizardSteps } from "#/components/ui/WizardSteps";
import { positionOptionsQuery } from "#/features/positions/positionsQueries";
import { ZusCodePicker } from "#/features/zus-codes/ZusCodePicker";
import { useAppForm } from "#/forms";
import { errorCode } from "#/forms/errors";
import { applyFieldErrors } from "#/forms/serverErrors";
import { ProblemError } from "#/lib/problem";
import {
	useAddEmploymentTerms,
	useCreateEmployment,
	useTerminateEmployment,
	useUpdateEmployment,
} from "./employmentsQueries";
import {
	AMOUNT_PATTERN,
	CONTRACT_TYPES,
	COST_KINDS_BY_KIND,
	EMPLOYMENT_KINDS,
	type EmploymentKind,
	emptyTerms,
	hasPosition,
	hasZusCodes,
	INSURANCE_PRESETS,
	INSURANCES,
	PAY_BASES_BY_KIND,
	type TermsValues,
	termsRequest,
	termsValues,
} from "./types";

export type EmploymentDialogHandle = {
	create: (employeeId: string) => void;
	edit: (employment: GetEmploymentResponse) => void;
	addTerms: (employment: GetEmploymentResponse) => void;
	terminate: (employment: GetEmploymentResponse) => void;
};

type FormTarget =
	| { mode: "create"; employeeId: string }
	| { mode: "edit"; employment: GetEmploymentResponse }
	| { mode: "terms"; employment: GetEmploymentResponse };
type Target = FormTarget | { mode: "terminate"; employment: GetEmploymentResponse };

export const EmploymentDialog = forwardRef<EmploymentDialogHandle>(
	function EmploymentDialog(_props, ref) {
		const [target, setTarget] = useState<Target | null>(null);
		useImperativeHandle(
			ref,
			() => ({
				create: (employeeId) => setTarget({ mode: "create", employeeId }),
				edit: (employment) => setTarget({ mode: "edit", employment }),
				addTerms: (employment) => setTarget({ mode: "terms", employment }),
				terminate: (employment) => setTarget({ mode: "terminate", employment }),
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
						{target?.mode === "terminate" ? (
							<TerminateForm employment={target.employment} onDone={close} />
						) : target ? (
							<EmploymentForm target={target} onDone={close} />
						) : null}
					</Modal.Container>
				</Modal.Backdrop>
			</Modal>
		);
	},
);

// Only shape and the amount format here; required fields depend on the kind and are checked by the database.
const schema = z.object({
	number: z.string().max(40, "maxLength"),
	kind: z.enum(EMPLOYMENT_KINDS),
	contractType: z.string(),
	signedAt: z.string(),
	startDate: z.string(),
	endDate: z.string(),
	validFrom: z.string(),
	positionId: z.string(),
	fteNumerator: z.string().regex(/^\s*\d*\s*$/, "invalid"),
	fteDenominator: z.string().regex(/^\s*\d*\s*$/, "invalid"),
	payBasis: z.string(),
	baseAmount: z.string().refine((value) => value === "" || AMOUNT_PATTERN.test(value), "invalid"),
	zusTitleCode: z.string(),
	zusPensionCode: z.string(),
	zusDisabilityCode: z.string(),
	insurances: z.array(z.string()),
	costKind: z.string(),
});

const FIELDS = [
	"number",
	"kind",
	"contractType",
	"signedAt",
	"startDate",
	"endDate",
	"validFrom",
	"positionId",
	"fteNumerator",
	"payBasis",
	"baseAmount",
	"zusTitleCode",
	"zusPensionCode",
	"zusDisabilityCode",
	"insurances",
	"costKind",
] as const;

type Step = "contract" | "pay" | "zus";

// Wizard steps: a new contract is contract → pay → ZUS and insurances (none for specific work); a new terms
// version is pay → ZUS; editing the contract itself is one step.
function stepsFor(mode: FormTarget["mode"], kind: EmploymentKind): Step[] {
	const terms: Step[] = hasZusCodes(kind) ? ["pay", "zus"] : ["pay"];
	if (mode === "edit") return ["contract"];
	return mode === "create" ? ["contract", ...terms] : terms;
}

const STEP_FIELDS: Record<Step, readonly string[]> = {
	contract: ["number", "kind", "contractType", "signedAt", "startDate", "endDate"],
	pay: [
		"validFrom",
		"positionId",
		"fteNumerator",
		"fteDenominator",
		"payBasis",
		"baseAmount",
		"costKind",
	],
	zus: ["zusTitleCode", "zusPensionCode", "zusDisabilityCode", "insurances"],
};

type ContractValues = {
	number: string;
	kind: EmploymentKind;
	contractType: string;
	signedAt: string;
	startDate: string;
	endDate: string;
	validFrom: string;
};

function initialValues(target: FormTarget): ContractValues & TermsValues {
	if (target.mode === "create") {
		return {
			number: "",
			kind: "employment",
			contractType: "indefinite",
			signedAt: "",
			startDate: "",
			endDate: "",
			validFrom: "",
			...emptyTerms("employment"),
		};
	}
	const { employment } = target;
	const kind = employment.kind as EmploymentKind;
	const last: GetEmploymentTerms | undefined = employment.termsHistory[0];
	return {
		number: employment.number,
		kind,
		contractType: employment.contractType ?? "",
		signedAt: employment.signedAt,
		startDate: employment.startDate,
		endDate: employment.endDate ?? "",
		validFrom: "",
		...(last ? termsValues(last) : emptyTerms(kind)),
	};
}

function EmploymentForm({ target, onDone }: { target: FormTarget; onDone: () => void }) {
	const { t } = useTranslation();
	const formId = useId();
	const navigate = useNavigate();
	const create = useCreateEmployment();
	const update = useUpdateEmployment();
	const addTerms = useAddEmploymentTerms();
	const [step, setStep] = useState(0);
	const { data: positions } = useQuery(positionOptionsQuery());

	const form = useAppForm({
		defaultValues: initialValues(target),
		validators: { onChange: schema },
		onSubmit: async ({ value, formApi }) => {
			const blank = (text: string) => (text.trim() === "" ? null : text);
			try {
				if (target.mode === "create") {
					const created = await create.mutateAsync({
						employeeId: target.employeeId,
						request: {
							number: blank(value.number),
							kind: value.kind,
							contractType: value.kind === "employment" ? blank(value.contractType) : null,
							signedAt: blank(value.signedAt),
							startDate: blank(value.startDate),
							endDate:
								value.contractType === "indefinite" && value.kind === "employment"
									? null
									: blank(value.endDate),
							...termsRequest(value.kind, value),
						},
					});
					toast.success(t("employments.create.created"));
					onDone();
					await navigate({ to: "/employments/$id", params: { id: created.id } });
					return;
				}
				const { employment } = target;
				if (target.mode === "edit") {
					await update.mutateAsync({
						id: employment.id,
						request: {
							rowVersion: employment.rowVersion,
							number: value.number,
							contractType: blank(value.contractType),
							signedAt: blank(value.signedAt),
							startDate: blank(value.startDate),
							endDate: value.contractType === "indefinite" ? null : blank(value.endDate),
						},
					});
					toast.success(t("employments.edit.saved"));
				} else {
					await addTerms.mutateAsync({
						id: employment.id,
						request: { validFrom: blank(value.validFrom), ...termsRequest(value.kind, value) },
					});
					toast.success(t("employments.terms.saved"));
				}
				onDone();
			} catch (error) {
				if (error instanceof ProblemError && error.problem.status === 409) {
					if (error.problem.reason) {
						toast.danger(t(`reasons.${error.problem.reason}`, t("common.conflict")));
					} else {
						toast.warning(t("common.conflict"));
						onDone();
					}
				} else if (
					!(error instanceof ProblemError) ||
					!applyFieldErrors(formApi, error.problem.errors, FIELDS)
				) {
					toast.danger(t("common.serverError"));
				} else {
					// The error may sit on a field of a step that is not shown — go to the first such step.
					const steps = stepsFor(target.mode, value.kind);
					const index = firstStepWithError(
						steps.map((item) => STEP_FIELDS[item]),
						error.problem.errors,
					);
					if (index !== null) setStep(index);
				}
			}
		},
	});

	const renderPay = (kind: EmploymentKind) => (
		<>
			{hasPosition(kind) ? (
				<>
					<form.Field name="positionId">
						{(field) => (
							<OptionSelect
								label={t("employments.fields.position")}
								value={field.state.value}
								options={(positions?.items ?? []).map((position) => ({
									id: position.id,
									label: position.isActive
										? position.name
										: `${position.name} (${t("employments.inactive")})`,
								}))}
								onChange={field.handleChange}
								onBlur={field.handleBlur}
								isRequired
								errorMessage={fieldError(t, field.state.meta)}
							/>
						)}
					</form.Field>
					<div className="grid grid-cols-2 gap-3">
						<form.AppField name="fteNumerator">
							{(field) => (
								<field.TextInput label={t("employments.fields.fteNumerator")} isRequired />
							)}
						</form.AppField>
						<form.AppField name="fteDenominator">
							{(field) => (
								<field.TextInput label={t("employments.fields.fteDenominator")} isRequired />
							)}
						</form.AppField>
					</div>
				</>
			) : null}
			{PAY_BASES_BY_KIND[kind].length > 1 ? (
				<form.AppField name="payBasis">
					{(field) => (
						<field.SelectInput
							label={t("employments.fields.payBasis")}
							options={PAY_BASES_BY_KIND[kind].map((basis) => ({
								id: basis,
								label: t(`employments.payBases.${basis}`),
							}))}
							isRequired
						/>
					)}
				</form.AppField>
			) : null}
			<form.Subscribe selector={(state) => state.values.payBasis}>
				{(payBasis) => (
					<form.AppField name="baseAmount">
						{(field) => (
							<field.TextInput
								label={t(
									`employments.amountLabels.${payBasis as "monthly" | "hourly" | "lumpSum"}`,
								)}
								isRequired
							/>
						)}
					</form.AppField>
				)}
			</form.Subscribe>
			<form.AppField name="costKind">
				{(field) => (
					<field.SelectInput
						label={t("employments.fields.costKind")}
						options={COST_KINDS_BY_KIND[kind].map((cost) => ({
							id: cost,
							label: t(`employments.costKinds.${cost}`),
						}))}
						isRequired
					/>
				)}
			</form.AppField>
		</>
	);

	const renderZus = (kind: EmploymentKind) => (
		<>
			<form.Field name="zusTitleCode">
				{(field) => (
					<ZusCodePicker
						label={t("employments.fields.zusTitleCode")}
						kind="title"
						value={field.state.value}
						onChange={field.handleChange}
						onBlur={field.handleBlur}
						isRequired
						errorMessage={fieldError(t, field.state.meta)}
					/>
				)}
			</form.Field>
			<div className="grid grid-cols-2 gap-3">
				<form.Field name="zusPensionCode">
					{(field) => (
						<ZusCodePicker
							label={t("employments.fields.zusPensionCode")}
							kind="pension"
							value={field.state.value}
							onChange={field.handleChange}
							onBlur={field.handleBlur}
							isRequired
							errorMessage={fieldError(t, field.state.meta)}
						/>
					)}
				</form.Field>
				<form.Field name="zusDisabilityCode">
					{(field) => (
						<ZusCodePicker
							label={t("employments.fields.zusDisabilityCode")}
							kind="disability"
							value={field.state.value}
							onChange={field.handleChange}
							onBlur={field.handleBlur}
							isRequired
							errorMessage={fieldError(t, field.state.meta)}
						/>
					)}
				</form.Field>
			</div>
			<form.Field name="insurances">
				{(field) => (
					<div className="flex flex-col gap-2">
						<CheckboxGroup
							value={field.state.value}
							onChange={field.handleChange}
							isInvalid={Boolean(fieldError(t, field.state.meta))}
						>
							<Label>{t("employments.fields.insurances")}</Label>
							{kind === "mandate" ? (
								<Description>{t("employments.insurancesHint")}</Description>
							) : null}
							{INSURANCES.map((insurance) => (
								<Checkbox key={insurance} value={insurance}>
									<Checkbox.Content>
										<Checkbox.Control>
											<Checkbox.Indicator />
										</Checkbox.Control>
										{t(`employments.insurances.${insurance}`)}
									</Checkbox.Content>
								</Checkbox>
							))}
						</CheckboxGroup>
						{kind === "mandate" ? (
							<div className="flex flex-wrap gap-2">
								{(["mandateSoleTitle", "mandateHealthOnly", "none"] as const).map((preset) => (
									<Button
										key={preset}
										size="sm"
										variant="tertiary"
										onPress={() => field.handleChange([...INSURANCE_PRESETS[preset]])}
									>
										{t(`employments.presets.${preset}`)}
									</Button>
								))}
							</div>
						) : null}
						<p className="text-sm text-danger">{fieldError(t, field.state.meta)}</p>
					</div>
				)}
			</form.Field>
		</>
	);

	const title =
		target.mode === "create"
			? t("employments.create.title")
			: target.mode === "edit"
				? t("employments.edit.title", { number: target.employment.number })
				: t("employments.terms.title", { number: target.employment.number });

	return (
		<Modal.Dialog className="form-dialog">
			<Modal.Header>
				<Modal.Heading>{title}</Modal.Heading>
			</Modal.Header>
			<form.Subscribe selector={(state) => [state.values.kind, state.values.contractType] as const}>
				{([kind, contractType]) => {
					const steps = stepsFor(target.mode, kind);
					const current = steps[Math.min(step, steps.length - 1)] ?? "contract";
					const isLastStep = step >= steps.length - 1;
					return (
						<>
							<Modal.Body>
								{steps.length > 1 ? (
									<WizardSteps
										steps={steps.map((item) => t(`employments.wizard.${item}`))}
										current={Math.min(step, steps.length - 1)}
										onSelect={setStep}
									/>
								) : null}
								{target.mode === "terms" && current === "pay" ? (
									<p className="mb-4 text-sm text-muted">{t("employments.terms.hint")}</p>
								) : null}
								<form
									id={formId}
									className="flex flex-col gap-4"
									noValidate
									onSubmit={(event) => {
										event.preventDefault();
										// Enter on an earlier step moves on; only the last step saves.
										if (isLastStep) void form.handleSubmit();
										else setStep(step + 1);
									}}
								>
									{current === "contract" ? (
										<>
											{target.mode === "create" ? (
												<form.Field
													name="kind"
													listeners={{
														onChange: ({ value }) => {
															const terms = emptyTerms(value);
															for (const [key, fieldValue] of Object.entries(terms)) {
																form.setFieldValue(key as keyof TermsValues, fieldValue);
															}
															form.setFieldValue(
																"contractType",
																value === "employment" ? "indefinite" : "",
															);
														},
													}}
												>
													{(field) => (
														<OptionSelect
															label={t("employments.fields.kind")}
															value={field.state.value}
															options={EMPLOYMENT_KINDS.map((item) => ({
																id: item,
																label: t(`employments.kinds.${item}`),
															}))}
															onChange={(item) =>
																item && field.handleChange(item as EmploymentKind)
															}
															isRequired
														/>
													)}
												</form.Field>
											) : null}
											<div className="grid gap-4 sm:grid-cols-2">
												<form.AppField name="number">
													{(field) => (
														<field.TextInput
															label={t("employments.fields.number")}
															description={
																target.mode === "create"
																	? t("employments.fields.numberHint")
																	: undefined
															}
															isRequired={target.mode !== "create"}
														/>
													)}
												</form.AppField>
												{kind === "employment" ? (
													<form.AppField name="contractType">
														{(field) => (
															<field.SelectInput
																label={t("employments.fields.contractType")}
																options={CONTRACT_TYPES.map((type) => ({
																	id: type,
																	label: t(`employments.contractTypes.${type}`),
																}))}
																isRequired
															/>
														)}
													</form.AppField>
												) : null}
											</div>
											<div className="grid gap-4 sm:grid-cols-3">
												<form.AppField name="signedAt">
													{(field) => (
														<field.DateInput label={t("employments.fields.signedAt")} isRequired />
													)}
												</form.AppField>
												<form.AppField name="startDate">
													{(field) => (
														<field.DateInput label={t("employments.fields.startDate")} isRequired />
													)}
												</form.AppField>
												{kind === "employment" && contractType === "indefinite" ? null : (
													<form.AppField name="endDate">
														{(field) => (
															<field.DateInput label={t("employments.fields.endDate")} isRequired />
														)}
													</form.AppField>
												)}
											</div>
										</>
									) : null}
									{current === "pay" ? (
										<>
											{target.mode === "terms" ? (
												<div className="grid gap-4 sm:grid-cols-2">
													<form.AppField name="validFrom">
														{(field) => (
															<field.DateInput
																label={t("employments.fields.validFrom")}
																isRequired
															/>
														)}
													</form.AppField>
												</div>
											) : null}
											{renderPay(kind)}
										</>
									) : null}
									{current === "zus" ? renderZus(kind) : null}
								</form>
							</Modal.Body>
							<Modal.Footer>
								<Button slot="close" variant="secondary">
									{t("common.cancel")}
								</Button>
								{step > 0 ? (
									<Button variant="tertiary" onPress={() => setStep(step - 1)}>
										{t("common.back")}
									</Button>
								) : null}
								<form.Subscribe selector={(state) => state.isSubmitting}>
									{(isSubmitting) => (
										<Button type="submit" form={formId} isPending={isSubmitting}>
											{isLastStep ? t("common.save") : t("common.continue")}
										</Button>
									)}
								</form.Subscribe>
							</Modal.Footer>
						</>
					);
				}}
			</form.Subscribe>
		</Modal.Dialog>
	);
}

type TranslateFn = ReturnType<typeof useTranslation>["t"];

function fieldError(
	t: TranslateFn,
	meta: { errors: ReadonlyArray<unknown>; isTouched: boolean },
): string | null {
	const code = errorCode(meta.errors);
	return code && meta.isTouched ? t(`validation.${code}`) : null;
}

function TerminateForm({
	employment,
	onDone,
}: {
	employment: GetEmploymentResponse;
	onDone: () => void;
}) {
	const { t } = useTranslation();
	const formId = useId();
	const terminate = useTerminateEmployment();

	const form = useAppForm({
		defaultValues: { terminatedAt: employment.terminatedAt ?? "" },
		// Shape only; it also lets a later edit clear the server's field errors (see applyFieldErrors).
		validators: { onChange: z.object({ terminatedAt: z.string() }) },
		onSubmit: async ({ value, formApi }) => {
			try {
				await terminate.mutateAsync({
					id: employment.id,
					request: {
						rowVersion: employment.rowVersion,
						terminatedAt: value.terminatedAt === "" ? null : value.terminatedAt,
					},
				});
				toast.success(
					value.terminatedAt === ""
						? t("employments.terminate.undone")
						: t("employments.terminate.saved"),
				);
				onDone();
			} catch (error) {
				if (error instanceof ProblemError && error.problem.status === 409) {
					if (error.problem.reason) {
						toast.danger(t(`reasons.${error.problem.reason}`, t("common.conflict")));
					} else {
						toast.warning(t("common.conflict"));
						onDone();
					}
				} else if (
					!(error instanceof ProblemError) ||
					!applyFieldErrors(formApi, error.problem.errors, ["terminatedAt"] as const)
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
					{t("employments.terminate.title", { number: employment.number })}
				</Modal.Heading>
			</Modal.Header>
			<Modal.Body>
				<p className="mb-4 text-sm text-muted">{t("employments.terminate.hint")}</p>
				<form
					id={formId}
					noValidate
					onSubmit={(event) => {
						event.preventDefault();
						void form.handleSubmit();
					}}
				>
					<form.AppField name="terminatedAt">
						{(field) => <field.DateInput label={t("employments.fields.terminatedAt")} />}
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
