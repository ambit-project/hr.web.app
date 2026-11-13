import { AlertDialog, Button, Chip, EmptyState, Modal, Spinner, Table, toast } from "@heroui/react";
import { useQuery } from "@tanstack/react-query";
import { Link as RouterLink } from "@tanstack/react-router";
import { forwardRef, useId, useImperativeHandle, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { z } from "zod";
import type { GetPpkReenrollmentItem, GetPpkReenrollmentResponse } from "#/api/models";
import { OpenIcon, openLinkClass } from "#/components/ui/OpenLink";
import { OptionSelect } from "#/components/ui/OptionSelect";
import { Notice } from "#/features/time-absence/Notice";
import { currentYear } from "#/features/time-absence/types";
import { useAppForm } from "#/forms";
import { onActionError, onSaveError, problemMessage } from "#/forms/feedback";
import { applyFieldErrors } from "#/forms/serverErrors";
import { ProblemError } from "#/lib/problem";
import { ppkReenrollmentQuery, useApplyPpkReenrollment, useMarkPpkInformed } from "./ppkQueries";
import {
	cycleYearOptions,
	groupReenrollment,
	REENROLLMENT_GROUPS,
	type ReenrollmentGroup,
	todayIso,
} from "./types";

/** "Ponowny zapis": the 4-year automatic re-enrolment of people who resigned (R12, R13). */
export function ReenrollmentTab({
	companyId,
	year,
	onYearChange,
}: {
	companyId: string;
	year: number;
	onYearChange: (year: number) => void;
}) {
	const { t } = useTranslation();
	const { data, isPending, error } = useQuery(ppkReenrollmentQuery(companyId, year));
	const notCycleYear =
		error instanceof ProblemError && error.problem.errors?.year?.includes("notCycleYear");

	return (
		<div className="flex flex-col gap-3">
			<p className="max-w-4xl text-sm text-muted">{t("ppk.reenrollment.hint")}</p>
			<OptionSelect
				className="w-40"
				label={t("ppk.reenrollment.year")}
				value={String(year)}
				options={cycleYearOptions(currentYear(), year).map((value) => ({
					id: String(value),
					label: String(value),
				}))}
				onChange={(value) => value && onYearChange(Number(value))}
			/>
			{isPending ? (
				<Spinner aria-label={t("common.loading")} />
			) : notCycleYear ? (
				<Notice>{t("ppk.reenrollment.notCycleYear")}</Notice>
			) : error || !data ? (
				<Notice status="danger">{problemMessage(t, error)}</Notice>
			) : (
				<Cycle companyId={companyId} cycle={data} />
			)}
		</div>
	);
}

function Cycle({ companyId, cycle }: { companyId: string; cycle: GetPpkReenrollmentResponse }) {
	const { t } = useTranslation();
	const dialog = useRef<InformedDialogHandle>(null);
	const groups = groupReenrollment(cycle.items);
	const rows: [string, string][] = [
		[t("ppk.reenrollment.informationDeadline"), cycle.informationDeadline],
		[t("ppk.reenrollment.restartDate"), cycle.restartDate],
		[t("ppk.reenrollment.informedAt"), cycle.informedAt ?? t("ppk.reenrollment.notInformed")],
		[t("ppk.reenrollment.appliedAt"), cycle.appliedAt ?? t("ppk.reenrollment.notApplied")],
	];

	return (
		<div className="flex flex-col gap-4">
			<dl className="grid max-w-xl grid-cols-[12rem_1fr] gap-x-4 gap-y-1 text-sm tabular-nums">
				{rows.map(([label, value]) => (
					<div key={label} className="contents">
						<dt className="text-muted">{label}</dt>
						<dd>{value}</dd>
					</div>
				))}
			</dl>
			<div className="flex flex-wrap gap-2">
				<Button
					variant="secondary"
					isDisabled={cycle.appliedAt !== null}
					onPress={() => dialog.current?.open(companyId, cycle)}
				>
					{t("ppk.reenrollment.inform.action")}
				</Button>
				<ApplyCycle companyId={companyId} cycle={cycle} />
			</div>
			{REENROLLMENT_GROUPS.map((group) => (
				<GroupTable
					key={group}
					group={group}
					restartDate={cycle.restartDate}
					items={groups[group]}
				/>
			))}
			<InformedDialog ref={dialog} />
		</div>
	);
}

function ApplyCycle({
	companyId,
	cycle,
}: {
	companyId: string;
	cycle: GetPpkReenrollmentResponse;
}) {
	const { t } = useTranslation();
	const apply = useApplyPpkReenrollment();

	const onApply = async () => {
		try {
			const result = await apply.mutateAsync({ id: companyId, year: cycle.year });
			if (result.restarted > 0) {
				toast.success(t("ppk.reenrollment.apply.done", { count: result.restarted }));
			} else {
				toast.info(t("ppk.reenrollment.apply.none"));
			}
		} catch (error) {
			onActionError(t, error);
		}
	};

	return (
		<AlertDialog>
			<Button isPending={apply.isPending}>{t("ppk.reenrollment.apply.action")}</Button>
			<AlertDialog.Backdrop isDismissable={false} isKeyboardDismissDisabled>
				<AlertDialog.Container>
					<AlertDialog.Dialog className="sm:max-w-[480px]">
						<AlertDialog.Header>
							<AlertDialog.Icon status="warning" />
							<AlertDialog.Heading>
								{t("ppk.reenrollment.apply.title", { date: cycle.restartDate })}
							</AlertDialog.Heading>
						</AlertDialog.Header>
						<AlertDialog.Body className="flex flex-col gap-2">
							<p>{t("ppk.reenrollment.apply.body", { date: cycle.restartDate })}</p>
							{cycle.informedAt === null ? (
								<p className="text-danger">{t("ppk.reenrollment.apply.needsInformation")}</p>
							) : null}
						</AlertDialog.Body>
						<AlertDialog.Footer>
							<Button slot="close" variant="tertiary">
								{t("common.cancel")}
							</Button>
							<Button slot="close" onPress={() => void onApply()}>
								{t("ppk.reenrollment.apply.action")}
							</Button>
						</AlertDialog.Footer>
					</AlertDialog.Dialog>
				</AlertDialog.Container>
			</AlertDialog.Backdrop>
		</AlertDialog>
	);
}

function GroupTable({
	group,
	restartDate,
	items,
}: {
	group: ReenrollmentGroup;
	restartDate: string;
	items: readonly GetPpkReenrollmentItem[];
}) {
	const { t } = useTranslation();
	const title = t(`ppk.reenrollment.groups.${group}`, { date: restartDate });

	return (
		<div className="flex flex-col gap-2">
			<h3 className="text-base font-semibold">
				{title} <span className="text-sm font-normal text-muted">({items.length})</span>
			</h3>
			<p className="max-w-3xl text-sm text-muted">{t(`ppk.reenrollment.groupHints.${group}`)}</p>
			<Table className="max-w-5xl">
				<Table.ScrollContainer>
					<Table.Content aria-label={title} className="min-w-[720px]">
						<Table.Header>
							<Table.Column isRowHeader>{t("ppk.employee")}</Table.Column>
							<Table.Column>{t("employees.fields.number")}</Table.Column>
							<Table.Column>{t("employees.fields.birthDate")}</Table.Column>
							<Table.Column className="text-right">{t("ppk.age")}</Table.Column>
							<Table.Column>{t("ppk.reenrollment.resignedFrom")}</Table.Column>
							<Table.Column>{t("ppk.reenrollment.state")}</Table.Column>
							<Table.Column className="w-10">
								<span className="sr-only">{t("common.open")}</span>
							</Table.Column>
						</Table.Header>
						<Table.Body
							renderEmptyState={() => (
								<EmptyState className="py-4 text-center text-sm text-muted">
									{t("ppk.reenrollment.groupEmpty")}
								</EmptyState>
							)}
						>
							{items.map((item) => (
								<Table.Row key={item.employeeId} id={item.employeeId}>
									<Table.Cell>
										{item.lastName} {item.firstName}
									</Table.Cell>
									<Table.Cell>{item.employeeNumber}</Table.Cell>
									<Table.Cell>{item.birthDate}</Table.Cell>
									<Table.Cell className="text-right">{item.age}</Table.Cell>
									<Table.Cell>{item.resignedFrom}</Table.Cell>
									<Table.Cell>
										<ItemState item={item} />
									</Table.Cell>
									<Table.Cell>
										<RouterLink
											to="/employees/$id"
											params={{ id: item.employeeId }}
											search={{ tab: "ppk" }}
											className={openLinkClass}
											aria-label={t("common.open")}
											title={t("common.open")}
										>
											<OpenIcon />
										</RouterLink>
									</Table.Cell>
								</Table.Row>
							))}
						</Table.Body>
					</Table.Content>
				</Table.ScrollContainer>
			</Table>
		</div>
	);
}

function ItemState({ item }: { item: GetPpkReenrollmentItem }) {
	const { t } = useTranslation();
	if (item.restarted) {
		return (
			<Chip size="sm" color="success">
				{t("ppk.reenrollment.restarted")}
			</Chip>
		);
	}
	if (item.resignedAfterInformation) {
		return (
			<Chip size="sm" color="warning">
				{t("ppk.reenrollment.resignedAgain")}
			</Chip>
		);
	}
	return <Chip size="sm">{t("ppk.reenrollment.waiting")}</Chip>;
}

type InformedTarget = { companyId: string; cycle: GetPpkReenrollmentResponse };

type InformedDialogHandle = {
	open: (companyId: string, cycle: GetPpkReenrollmentResponse) => void;
};

const InformedDialog = forwardRef<InformedDialogHandle>(function InformedDialog(_props, ref) {
	const [target, setTarget] = useState<InformedTarget | null>(null);
	useImperativeHandle(
		ref,
		() => ({ open: (companyId, cycle) => setTarget({ companyId, cycle }) }),
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
					{target ? <InformedForm target={target} onDone={close} /> : null}
				</Modal.Container>
			</Modal.Backdrop>
		</Modal>
	);
});

// ppk_app.reenrollment_inform: not from the future, in the cycle year before the restart date.
const informedSchema = z.object({
	informedAt: z
		.string()
		.min(1, "required")
		.refine((value) => value <= todayIso(), "future"),
});

function InformedForm({ target, onDone }: { target: InformedTarget; onDone: () => void }) {
	const { t } = useTranslation();
	const formId = useId();
	const inform = useMarkPpkInformed();
	const { cycle } = target;

	const form = useAppForm({
		defaultValues: { informedAt: cycle.informedAt ?? todayIso() },
		validators: { onChange: informedSchema },
		onSubmit: async ({ value, formApi }) => {
			try {
				await inform.mutateAsync({
					id: target.companyId,
					year: cycle.year,
					informedAt: value.informedAt,
				});
				toast.success(t("ppk.reenrollment.inform.saved"));
				onDone();
			} catch (error) {
				onSaveError(
					t,
					error,
					(errors) => applyFieldErrors(formApi, errors, ["informedAt"] as const),
					onDone,
				);
			}
		},
	});

	return (
		<Modal.Dialog className="form-dialog">
			<Modal.Header>
				<Modal.Heading>{t("ppk.reenrollment.inform.title", { year: cycle.year })}</Modal.Heading>
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
					<p className="text-sm text-muted sm:col-span-2">
						{t("ppk.reenrollment.inform.hint", { deadline: cycle.informationDeadline })}
					</p>
					<form.AppField name="informedAt">
						{(field) => <field.DateInput label={t("ppk.reenrollment.informedAt")} isRequired />}
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
