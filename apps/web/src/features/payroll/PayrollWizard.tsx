import { Button, Checkbox, Modal, Spinner, toast } from "@heroui/react";
import { useQuery } from "@tanstack/react-query";
import { useNavigate } from "@tanstack/react-router";
import type { TFunction } from "i18next";
import { forwardRef, useId, useImperativeHandle, useState } from "react";
import { useTranslation } from "react-i18next";
import { z } from "zod";
import { WizardSteps } from "#/components/ui/WizardSteps";
import { todayIso } from "#/features/ppk/types";
import { Notice } from "#/features/time-absence/Notice";
import { useAppForm } from "#/forms";
import { onActionError, onSaveError } from "#/forms/feedback";
import { applyFieldErrors } from "#/forms/serverErrors";
import {
	payrollMembersQuery,
	payrollQuery,
	payrollsQuery,
	useCalculatePayroll,
	useCreatePayroll,
	useSetPayrollMembers,
} from "./payrollQueries";
import {
	formatPeriod,
	lastOfMonth,
	latestPaymentDate,
	memberChanges,
	monthOptions,
	PAYROLL_KINDS,
	type PayrollKind,
} from "./types";

export type PayrollWizardHandle = {
	create: (companyId: string, year: number) => void;
	/** A correction of a closed list: the first step comes with the list chosen. */
	correct: (payroll: { companyId: string; id: string; periodFrom: string }) => void;
};

type Target = { companyId: string; year: number; correctsPayrollId?: string };

/** New list in three steps: kind and period (creates the list) → members to exclude → summary. */
export const PayrollWizard = forwardRef<PayrollWizardHandle>(function PayrollWizard(_props, ref) {
	const [target, setTarget] = useState<Target | null>(null);
	useImperativeHandle(
		ref,
		() => ({
			create: (companyId, year) => setTarget({ companyId, year }),
			correct: (payroll) =>
				setTarget({
					companyId: payroll.companyId,
					year: Number(payroll.periodFrom.slice(0, 4)),
					correctsPayrollId: payroll.id,
				}),
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
					{target ? <Wizard target={target} onDone={close} /> : null}
				</Modal.Container>
			</Modal.Backdrop>
		</Modal>
	);
});

const STEPS = ["kind", "members", "summary"] as const;

function Wizard({ target, onDone }: { target: Target; onDone: () => void }) {
	const { t } = useTranslation();
	const [step, setStep] = useState(0);
	const [createdId, setCreatedId] = useState<string | null>(null);
	const isCorrection = Boolean(target.correctsPayrollId);

	return (
		<Modal.Dialog className="form-dialog">
			<Modal.Header>
				<Modal.Heading>
					{isCorrection ? t("payroll.wizard.correctionTitle") : t("payroll.wizard.title")}
				</Modal.Heading>
			</Modal.Header>
			{createdId === null ? (
				<KindStep
					target={target}
					onCancel={onDone}
					onCreated={(id) => {
						setCreatedId(id);
						setStep(1);
					}}
				/>
			) : step === 1 ? (
				<MembersStep id={createdId} onNext={() => setStep(2)} />
			) : (
				<SummaryStep id={createdId} onDone={onDone} onBack={() => setStep(1)} />
			)}
		</Modal.Dialog>
	);
}

function Steps({ current, onSelect }: { current: number; onSelect?: (index: number) => void }) {
	const { t } = useTranslation();
	return (
		<WizardSteps
			steps={STEPS.map((item) => t(`payroll.wizard.steps.${item}`))}
			current={current}
			// The list exists after the first step, so going back there would create a second one.
			onSelect={(index) => index > 0 && onSelect?.(index)}
		/>
	);
}

// Shape only; the rules (one regular list per kind and month, payment date not before the period, a closed regular
// list to correct) are checked by payroll_app.payroll_create.
const schema = z
	.object({
		kind: z.enum(PAYROLL_KINDS),
		period: z.string(),
		paymentDate: z.string().min(1, "required"),
		correctsPayrollId: z.string(),
	})
	.superRefine((value, context) => {
		if (value.kind === "correction" && value.correctsPayrollId === "") {
			context.addIssue({ code: "custom", path: ["correctsPayrollId"], message: "required" });
		}
		if (value.kind !== "correction" && value.period === "") {
			context.addIssue({ code: "custom", path: ["period"], message: "required" });
		}
	});

const FIELDS = ["kind", "period", "paymentDate", "correctsPayrollId"] as const;

function KindStep({
	target,
	onCancel,
	onCreated,
}: {
	target: Target;
	onCancel: () => void;
	onCreated: (id: string) => void;
}) {
	const { t } = useTranslation();
	const formId = useId();
	const create = useCreatePayroll();
	const { data: lists } = useQuery(payrollsQuery(target.companyId, target.year));
	// Only a closed regular list can be corrected (correctsPayrollId invalid otherwise).
	const correctable = (lists ?? []).filter(
		(item) => item.status === "closed" && item.kind !== "correction",
	);
	const months = monthOptions(target.year);
	const thisMonth = `${todayIso().slice(0, 7)}-01`;
	const period = months.includes(thisMonth) ? thisMonth : (months[0] ?? thisMonth);

	const form = useAppForm({
		defaultValues: {
			kind: (target.correctsPayrollId ? "correction" : "employment") as PayrollKind,
			period,
			paymentDate: target.correctsPayrollId ? todayIso() : lastOfMonth(period),
			correctsPayrollId: target.correctsPayrollId ?? "",
		},
		validators: { onChange: schema },
		onSubmit: async ({ value, formApi }) => {
			const correction = value.kind === "correction";
			try {
				const created = await create.mutateAsync({
					companyId: target.companyId,
					request: {
						kind: value.kind,
						period: correction ? null : value.period,
						paymentDate: value.paymentDate,
						correctsPayrollId: correction ? value.correctsPayrollId : null,
					},
				});
				onCreated(created.id);
			} catch (error) {
				onSaveError(t, error, (errors) => applyFieldErrors(formApi, errors, FIELDS), onCancel);
			}
		},
	});

	return (
		<>
			<Modal.Body>
				<Steps current={0} />
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
						<form.AppField
							name="kind"
							listeners={{
								onChange: ({ value }) => {
									if (value === "correction") form.setFieldValue("paymentDate", todayIso());
								},
							}}
						>
							{(field) => (
								<field.SelectInput
									label={t("payroll.fields.kind")}
									options={PAYROLL_KINDS.map((kind) => ({
										id: kind,
										label: t(`payroll.kinds.${kind}`),
									}))}
									isRequired
								/>
							)}
						</form.AppField>
					</div>
					<form.Subscribe selector={(state) => state.values.kind}>
						{(kind) =>
							kind === "correction" ? (
								<div className="flex flex-col gap-2 sm:col-span-2">
									{correctable.length === 0 ? (
										<Notice>{t("payroll.wizard.noClosed")}</Notice>
									) : null}
									<form.AppField name="correctsPayrollId">
										{(field) => (
											<field.SelectInput
												label={t("payroll.wizard.corrects")}
												options={correctable.map((item) => ({
													id: item.id,
													label: `${item.number} · ${t(`payroll.kinds.${item.kind as PayrollKind}`)} · ${formatPeriod(item.periodFrom)}`,
												}))}
												isRequired
											/>
										)}
									</form.AppField>
									<p className="text-xs text-muted">{t("payroll.wizard.correctsHint")}</p>
								</div>
							) : (
								<div className="flex flex-col gap-1">
									<form.AppField
										name="period"
										listeners={{
											onChange: ({ value }) => {
												if (value) form.setFieldValue("paymentDate", lastOfMonth(value));
											},
										}}
									>
										{(field) => (
											<field.SelectInput
												label={t("payroll.wizard.month")}
												options={months.map((month) => ({
													id: month,
													label: formatPeriod(month),
												}))}
												isRequired
											/>
										)}
									</form.AppField>
									<span className="text-xs text-muted">{t("payroll.wizard.periodHint")}</span>
								</div>
							)
						}
					</form.Subscribe>
					<form.Subscribe selector={(state) => [state.values.kind, state.values.period] as const}>
						{([kind, selectedPeriod]) => (
							<div className="flex flex-col gap-1">
								<form.AppField name="paymentDate">
									{(field) => (
										<field.DateInput label={t("payroll.fields.paymentDate")} isRequired />
									)}
								</form.AppField>
								{kind !== "correction" && selectedPeriod ? (
									<span className="text-xs text-muted">
										{t("payroll.wizard.paymentDateHint", {
											date: latestPaymentDate(selectedPeriod),
										})}
									</span>
								) : null}
							</div>
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
							{t("payroll.wizard.createAndContinue")}
						</Button>
					)}
				</form.Subscribe>
			</Modal.Footer>
		</>
	);
}

function MembersStep({ id, onNext }: { id: string; onNext: () => void }) {
	const { t } = useTranslation();
	const { data: payroll } = useQuery(payrollQuery(id));
	const { data: members, isPending } = useQuery(payrollMembersQuery(id));
	const setMembers = useSetPayrollMembers();
	// null until the person changes something: the server's exclusions are the starting point.
	const [excluded, setExcluded] = useState<Set<string> | null>(null);
	const current =
		excluded ??
		new Set(
			(members ?? []).filter((member) => member.excluded).map((member) => member.employmentId),
		);
	const isCorrection = payroll?.kind === "correction";

	const toggle = (employmentId: string, include: boolean) => {
		const next = new Set(current);
		if (include) next.delete(employmentId);
		else next.add(employmentId);
		setExcluded(next);
	};

	const save = async () => {
		const changes = memberChanges(members ?? [], current);
		try {
			if (changes.exclude.length > 0) {
				await setMembers.mutateAsync({
					id,
					request: { rowVersion: null, employmentIds: changes.exclude, excluded: true },
				});
			}
			if (changes.include.length > 0) {
				await setMembers.mutateAsync({
					id,
					request: { rowVersion: null, employmentIds: changes.include, excluded: false },
				});
			}
			onNext();
		} catch (error) {
			onActionError(t, error);
		}
	};

	return (
		<>
			<Modal.Body>
				<Steps current={1} />
				<div className="flex flex-col gap-3">
					<Notice status="accent">{t("payroll.wizard.alreadyCreated")}</Notice>
					<p className="text-sm text-muted">
						{isCorrection
							? t("payroll.wizard.correctionMembersHint")
							: t("payroll.wizard.membersHint")}
					</p>
					{isPending ? (
						<Spinner aria-label={t("common.loading")} />
					) : (members ?? []).length === 0 ? (
						<p className="text-sm text-muted">{t("payroll.wizard.membersEmpty")}</p>
					) : (
						<ul className="grid gap-2 sm:grid-cols-2">
							{(members ?? []).map((member) => (
								<li key={member.employmentId}>
									<Checkbox
										isSelected={!current.has(member.employmentId)}
										onChange={(selected) => toggle(member.employmentId, selected)}
									>
										<Checkbox.Content>
											<Checkbox.Control>
												<Checkbox.Indicator />
											</Checkbox.Control>
											<span>
												{member.lastName} {member.firstName}
												<span className="block text-xs text-muted">
													{t("payroll.fields.contract")} {member.employmentNumber}
												</span>
											</span>
										</Checkbox.Content>
									</Checkbox>
								</li>
							))}
						</ul>
					)}
				</div>
			</Modal.Body>
			<Modal.Footer>
				<Button slot="close" variant="secondary">
					{t("common.cancel")}
				</Button>
				<Button isPending={setMembers.isPending} onPress={() => void save()}>
					{t("common.continue")}
				</Button>
			</Modal.Footer>
		</>
	);
}

function SummaryStep({
	id,
	onDone,
	onBack,
}: {
	id: string;
	onDone: () => void;
	onBack: () => void;
}) {
	const { t } = useTranslation();
	const navigate = useNavigate();
	const { data: payroll } = useQuery(payrollQuery(id));
	const calculate = useCalculatePayroll();

	const open = async () => {
		onDone();
		await navigate({ to: "/payrolls/$id", params: { id } });
	};

	const calculateNow = async () => {
		try {
			await calculate.mutateAsync({ id, rowVersion: payroll?.rowVersion ?? null });
			toast.success(t("payroll.page.calculated"));
			await open();
		} catch (error) {
			onActionError(t, error);
		}
	};

	return (
		<>
			<Modal.Body>
				<Steps current={2} onSelect={() => onBack()} />
				{payroll ? (
					<div className="flex flex-col gap-3">
						<p className="text-sm text-muted">{t("payroll.wizard.summaryHint")}</p>
						<SummaryList t={t} payroll={payroll} />
					</div>
				) : (
					<Spinner aria-label={t("common.loading")} />
				)}
			</Modal.Body>
			<Modal.Footer>
				<Button variant="secondary" onPress={() => void open()}>
					{t("payroll.wizard.finish")}
				</Button>
				<Button isPending={calculate.isPending} onPress={() => void calculateNow()}>
					{t("payroll.wizard.calculateNow")}
				</Button>
			</Modal.Footer>
		</>
	);
}

function SummaryList({
	t,
	payroll,
}: {
	t: TFunction<"translation">;
	payroll: {
		number: string;
		kind: string;
		periodFrom: string;
		paymentDate: string;
		correctsNumber?: string | null;
		memberCount: number;
		excludedCount: number;
	};
}) {
	const rows: [string, string][] = [
		[t("payroll.fields.number"), payroll.number],
		[t("payroll.fields.kind"), t(`payroll.kinds.${payroll.kind as PayrollKind}`)],
		[t("payroll.fields.period"), formatPeriod(payroll.periodFrom)],
		[t("payroll.fields.paymentDate"), payroll.paymentDate],
		[
			t("payroll.page.members"),
			t("payroll.page.membersCount", {
				count: payroll.memberCount,
				excluded: payroll.excludedCount,
			}),
		],
	];
	if (payroll.correctsNumber)
		rows.splice(2, 0, [t("payroll.fields.corrects"), payroll.correctsNumber]);
	return (
		<dl className="grid grid-cols-[12rem_1fr] gap-x-4 gap-y-2 text-sm">
			{rows.map(([label, value]) => (
				<div key={label} className="contents">
					<dt className="text-muted">{label}</dt>
					<dd>{value}</dd>
				</div>
			))}
		</dl>
	);
}
