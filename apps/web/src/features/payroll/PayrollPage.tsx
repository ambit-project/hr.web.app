import { AlertDialog, Button, EmptyState, Spinner, Switch, Table, toast } from "@heroui/react";
import { useQuery } from "@tanstack/react-query";
import { getRouteApi, Link as RouterLink, useNavigate } from "@tanstack/react-router";
import { type ReactNode, useRef } from "react";
import { useTranslation } from "react-i18next";
import type { GetPayrollResponse } from "#/api/models";
import { OpenIcon, openLinkClass } from "#/components/ui/OpenLink";
import { Notice } from "#/features/time-absence/Notice";
import { onActionError, problemMessage } from "#/forms/feedback";
import { formatDecimal } from "#/lib/money";
import { PayrollStatusChip } from "./PayrollStatusChip";
import { LateIcon } from "./PayrollsPage";
import { PayrollWizard, type PayrollWizardHandle } from "./PayrollWizard";
import { PpkPaidDialog, type PpkPaidDialogHandle } from "./PpkPaidDialog";
import {
	payrollEntriesQuery,
	payrollMembersQuery,
	payrollQuery,
	useCalculatePayroll,
	useClosePayroll,
	useDeletePayroll,
	useSetPayrollMembers,
} from "./payrollQueries";
import { displayStatus, ENTRY_AMOUNTS, type PayrollKind, sumEntries } from "./types";
import { WarningsIcon } from "./Warnings";

const route = getRouteApi("/payrolls/$id");

/** One list: header with actions, summary, payslips and members. */
export function PayrollPage() {
	const { t } = useTranslation();
	const { id } = route.useParams();
	const { data: payroll, isPending, error } = useQuery(payrollQuery(id));
	const wizard = useRef<PayrollWizardHandle>(null);
	const ppkPaid = useRef<PpkPaidDialogHandle>(null);

	if (error) return <Notice status="danger">{problemMessage(t, error)}</Notice>;
	if (isPending || !payroll) return <Spinner aria-label={t("common.loading")} />;

	const status = displayStatus(payroll);
	const kind = payroll.kind as PayrollKind;
	const year = Number(payroll.periodFrom.slice(0, 4));
	const rows: [string, ReactNode][] = [
		[t("payroll.fields.kind"), t(`payroll.kinds.${kind}`)],
		[t("payroll.fields.period"), `${payroll.periodFrom} – ${payroll.periodTo}`],
		[
			t("payroll.fields.paymentDate"),
			<span key="payment" className="inline-flex items-center gap-1">
				{payroll.paymentDate}
				{payroll.paymentDateLate ? <LateIcon label={t("payroll.paymentDateLate")} /> : null}
			</span>,
		],
		[
			t("payroll.page.members"),
			t("payroll.page.membersCount", {
				count: payroll.memberCount,
				excluded: payroll.excludedCount,
			}),
		],
	];
	if (payroll.correctsPayrollId && payroll.correctsNumber) {
		rows.splice(1, 0, [
			t("payroll.fields.corrects"),
			<RouterLink
				key="corrects"
				to="/payrolls/$id"
				params={{ id: payroll.correctsPayrollId }}
				className="text-link hover:underline"
			>
				{payroll.correctsNumber}
			</RouterLink>,
		]);
	}
	if (payroll.calculatedAt) {
		rows.push([t("payroll.page.calculatedAt"), formatTimestamp(payroll.calculatedAt)]);
	}
	if (payroll.closedAt) rows.push([t("payroll.page.closedAt"), formatTimestamp(payroll.closedAt)]);
	if (payroll.ppkPaidOn) rows.push([t("payroll.page.ppkPaidOn"), payroll.ppkPaidOn]);

	return (
		<section className="flex flex-col gap-4">
			<RouterLink
				to="/payrolls"
				search={{ companyId: payroll.companyId, year }}
				className="text-sm text-muted hover:underline"
			>
				← {t("payroll.page.back")}
			</RouterLink>
			<header className="flex flex-wrap items-center gap-3">
				<h1 className="text-2xl font-semibold">
					{t("payroll.page.title", { number: payroll.number })}
				</h1>
				<PayrollStatusChip payroll={payroll} />
			</header>
			<dl className="grid max-w-2xl grid-cols-[12rem_1fr] gap-x-4 gap-y-2 text-sm">
				{rows.map(([label, value]) => (
					<div key={label} className="contents">
						<dt className="text-muted">{label}</dt>
						<dd>{value}</dd>
					</div>
				))}
			</dl>
			<PayrollActions
				payroll={payroll}
				onCorrect={() =>
					wizard.current?.correct({
						companyId: payroll.companyId,
						id: payroll.id,
						periodFrom: payroll.periodFrom,
					})
				}
				onPpkPaid={() => ppkPaid.current?.open(payroll)}
			/>
			{status === "open" ? <Notice status="default">{t("payroll.page.openNotice")}</Notice> : null}
			{status === "stale" ? <Notice>{t("payroll.page.staleNotice")}</Notice> : null}
			{status === "closed" ? (
				<Notice status="success">{t("payroll.page.closedNotice")}</Notice>
			) : null}
			{payroll.blockedCount > 0 ? (
				<Notice status="danger">
					{t("payroll.page.blockedNotice", { count: payroll.blockedCount })}
				</Notice>
			) : null}
			{payroll.warningCount > 0 ? (
				<Notice>{t("payroll.page.warningsNotice", { count: payroll.warningCount })}</Notice>
			) : null}
			<SummaryTable payroll={payroll} />
			<h2 className="pt-2 text-lg font-semibold">{t("payroll.page.entries")}</h2>
			<EntriesTable id={payroll.id} />
			<MembersSection payroll={payroll} />
			<PayrollWizard ref={wizard} />
			<PpkPaidDialog ref={ppkPaid} />
		</section>
	);
}

function formatTimestamp(value: string): string {
	return new Intl.DateTimeFormat("pl-PL", {
		dateStyle: "short",
		timeStyle: "short",
		timeZone: "Europe/Warsaw",
	}).format(new Date(value));
}

function PayrollActions({
	payroll,
	onCorrect,
	onPpkPaid,
}: {
	payroll: GetPayrollResponse;
	onCorrect: () => void;
	onPpkPaid: () => void;
}) {
	const { t } = useTranslation();
	const navigate = useNavigate();
	const calculate = useCalculatePayroll();
	const close = useClosePayroll();
	const remove = useDeletePayroll();
	const status = displayStatus(payroll);
	const versioned = { id: payroll.id, rowVersion: payroll.rowVersion };

	if (status === "closed") {
		// A correction corrects a regular list; a correction of a correction does not exist (R4).
		return (
			<div className="flex flex-wrap gap-2">
				{payroll.kind === "correction" ? null : (
					<Button variant="secondary" onPress={onCorrect}>
						{t("payroll.page.correction")}
					</Button>
				)}
				<Button variant="secondary" onPress={onPpkPaid}>
					{t("payroll.page.ppkPaid.action")}
				</Button>
			</div>
		);
	}

	return (
		<div className="flex flex-wrap gap-2">
			<Button
				isPending={calculate.isPending}
				variant={status === "calculated" ? "secondary" : "primary"}
				onPress={() =>
					calculate.mutate(versioned, {
						onSuccess: () => toast.success(t("payroll.page.calculated")),
						onError: (error) => onActionError(t, error),
					})
				}
			>
				{status === "open" ? t("payroll.page.calculate") : t("payroll.page.recalculate")}
			</Button>
			{status === "calculated" ? (
				<Confirm
					label={t("payroll.page.close.action")}
					title={t("payroll.page.close.title", { number: payroll.number })}
					body={t("payroll.page.close.body")}
					status="warning"
					onConfirm={() =>
						close.mutate(versioned, {
							onSuccess: () =>
								toast.success(t("payroll.page.close.done", { number: payroll.number })),
							onError: (error) => onActionError(t, error),
						})
					}
				/>
			) : null}
			<Confirm
				label={t("payroll.page.delete.action")}
				title={t("payroll.page.delete.title", { number: payroll.number })}
				body={t("payroll.page.delete.body")}
				status="danger"
				onConfirm={() =>
					remove.mutate(versioned, {
						onSuccess: () => {
							toast.success(t("payroll.page.delete.done", { number: payroll.number }));
							void navigate({
								to: "/payrolls",
								search: {
									companyId: payroll.companyId,
									year: Number(payroll.periodFrom.slice(0, 4)),
								},
							});
						},
						onError: (error) => onActionError(t, error),
					})
				}
			/>
		</div>
	);
}

/** An action confirmed in an alert that only "Anuluj" or the action closes. */
function Confirm({
	label,
	title,
	body,
	status,
	onConfirm,
}: {
	label: string;
	title: string;
	body: string;
	status: "warning" | "danger";
	onConfirm: () => void;
}) {
	const { t } = useTranslation();
	return (
		<AlertDialog>
			<Button variant={status === "danger" ? "danger-soft" : "primary"}>{label}</Button>
			<AlertDialog.Backdrop isDismissable={false} isKeyboardDismissDisabled>
				<AlertDialog.Container>
					<AlertDialog.Dialog className="sm:max-w-[520px]">
						<AlertDialog.Header>
							<AlertDialog.Icon status={status} />
							<AlertDialog.Heading>{title}</AlertDialog.Heading>
						</AlertDialog.Header>
						<AlertDialog.Body>
							<p>{body}</p>
						</AlertDialog.Body>
						<AlertDialog.Footer>
							<Button slot="close" variant="tertiary">
								{t("common.cancel")}
							</Button>
							<Button
								slot="close"
								variant={status === "danger" ? "danger" : "primary"}
								onPress={onConfirm}
							>
								{label}
							</Button>
						</AlertDialog.Footer>
					</AlertDialog.Dialog>
				</AlertDialog.Container>
			</AlertDialog.Backdrop>
		</AlertDialog>
	);
}

/** Totals of the list from payroll_get, one row, the same columns as the payslips. */
function SummaryTable({ payroll }: { payroll: GetPayrollResponse }) {
	const { t } = useTranslation();
	return (
		<Table>
			<Table.ScrollContainer>
				<Table.Content aria-label={t("payroll.page.totals")} className="min-w-[960px]">
					<Table.Header>
						<Table.Column isRowHeader>{t("payroll.page.totals")}</Table.Column>
						{ENTRY_AMOUNTS.map((key) => (
							<Table.Column key={key} className="text-right">
								{t(`payroll.fields.${key}`)}
							</Table.Column>
						))}
					</Table.Header>
					<Table.Body>
						<Table.Row id="totals">
							<Table.Cell>
								{t("payroll.fields.entryCount")}: {payroll.entryCount}
							</Table.Cell>
							{ENTRY_AMOUNTS.map((key) => (
								<Table.Cell key={key} className="text-right font-medium">
									{formatDecimal(payroll[key])}
								</Table.Cell>
							))}
						</Table.Row>
					</Table.Body>
				</Table.Content>
			</Table.ScrollContainer>
		</Table>
	);
}

function EntriesTable({ id }: { id: string }) {
	const { t } = useTranslation();
	const { data, isPending, error } = useQuery(payrollEntriesQuery(id));
	if (error) return <Notice status="danger">{problemMessage(t, error)}</Notice>;
	if (isPending) return <Spinner aria-label={t("common.loading")} />;
	const entries = data ?? [];
	const totals = sumEntries(entries);
	const columns = ENTRY_AMOUNTS.filter((key) => key !== "employerCost");

	return (
		<Table>
			<Table.ScrollContainer>
				<Table.Content aria-label={t("payroll.page.entries")} className="min-w-[1180px]">
					<Table.Header>
						<Table.Column isRowHeader>{t("payroll.fields.person")}</Table.Column>
						<Table.Column>{t("payroll.fields.contract")}</Table.Column>
						{columns.map((key) => (
							<Table.Column key={key} className="text-right">
								{t(`payroll.fields.${key}`)}
							</Table.Column>
						))}
						<Table.Column className="w-10">
							<span className="sr-only">{t("payroll.fields.warnings")}</span>
						</Table.Column>
						<Table.Column className="w-12">
							<span className="sr-only">{t("common.open")}</span>
						</Table.Column>
					</Table.Header>
					<Table.Body
						renderEmptyState={() => (
							<EmptyState className="py-8 text-center text-sm text-muted">
								{t("payroll.page.entriesEmpty")}
							</EmptyState>
						)}
					>
						{[
							...entries.map((entry) => (
								<Table.Row key={entry.id} id={entry.id}>
									<Table.Cell>
										<RouterLink
											to="/payroll-entries/$id"
											params={{ id: entry.id }}
											className="text-link hover:underline"
										>
											{entry.lastName} {entry.firstName}
										</RouterLink>
									</Table.Cell>
									<Table.Cell>{entry.employmentNumber}</Table.Cell>
									{columns.map((key) => (
										<Table.Cell key={key} className="text-right">
											{formatDecimal(entry[key])}
										</Table.Cell>
									))}
									<Table.Cell>
										{entry.warnings.length > 0 ? <WarningsIcon warnings={entry.warnings} /> : null}
									</Table.Cell>
									<Table.Cell>
										<RouterLink
											to="/payroll-entries/$id"
											params={{ id: entry.id }}
											className={openLinkClass}
											aria-label={t("common.open")}
											title={t("common.open")}
										>
											<OpenIcon />
										</RouterLink>
									</Table.Cell>
								</Table.Row>
							)),
							...(entries.length > 1
								? [
										<Table.Row key="totals" id="totals">
											<Table.Cell className="font-semibold">{t("payroll.page.totals")}</Table.Cell>
											<Table.Cell>{entries.length}</Table.Cell>
											{columns.map((key) => (
												<Table.Cell key={key} className="text-right font-semibold">
													{formatDecimal(totals[key])}
												</Table.Cell>
											))}
											<Table.Cell>{null}</Table.Cell>
											<Table.Cell>{null}</Table.Cell>
										</Table.Row>,
									]
								: []),
						]}
					</Table.Body>
				</Table.Content>
			</Table.ScrollContainer>
		</Table>
	);
}

/** Members of the list with a switch to exclude a contract (R3); closed lists show the members read-only. */
function MembersSection({ payroll }: { payroll: GetPayrollResponse }) {
	const { t } = useTranslation();
	const { data, isPending } = useQuery(payrollMembersQuery(payroll.id));
	const setMembers = useSetPayrollMembers();
	const editable = payroll.status !== "closed";

	return (
		<div className="flex flex-col gap-2 pt-2">
			<h2 className="text-lg font-semibold">{t("payroll.page.membersTitle")}</h2>
			{editable ? (
				<p className="max-w-3xl text-sm text-muted">{t("payroll.page.membersHint")}</p>
			) : null}
			{isPending ? (
				<Spinner aria-label={t("common.loading")} />
			) : (
				<Table className="max-w-3xl">
					<Table.ScrollContainer>
						<Table.Content aria-label={t("payroll.page.membersTitle")} className="min-w-[560px]">
							<Table.Header>
								<Table.Column isRowHeader>{t("payroll.fields.person")}</Table.Column>
								<Table.Column>{t("payroll.fields.contract")}</Table.Column>
								<Table.Column>{t("payroll.page.included")}</Table.Column>
							</Table.Header>
							<Table.Body
								renderEmptyState={() => (
									<EmptyState className="py-8 text-center text-sm text-muted">
										{t("payroll.wizard.membersEmpty")}
									</EmptyState>
								)}
							>
								{(data ?? []).map((member) => (
									<Table.Row key={member.employmentId} id={member.employmentId}>
										<Table.Cell>
											<RouterLink
												to="/employees/$id"
												params={{ id: member.employeeId }}
												className="text-link hover:underline"
											>
												{member.lastName} {member.firstName}
											</RouterLink>
										</Table.Cell>
										<Table.Cell>
											<RouterLink
												to="/employments/$id"
												params={{ id: member.employmentId }}
												className="text-link hover:underline"
											>
												{member.employmentNumber}
											</RouterLink>
										</Table.Cell>
										<Table.Cell>
											{editable ? (
												<Switch
													size="sm"
													isSelected={!member.excluded}
													isDisabled={setMembers.isPending}
													aria-label={
														member.excluded ? t("payroll.page.include") : t("payroll.page.exclude")
													}
													onChange={(selected) =>
														setMembers.mutate(
															{
																id: payroll.id,
																request: {
																	rowVersion: payroll.rowVersion,
																	employmentIds: [member.employmentId],
																	excluded: !selected,
																},
															},
															{
																onSuccess: () => toast.success(t("payroll.page.membersSaved")),
																onError: (error) => onActionError(t, error),
															},
														)
													}
												>
													<Switch.Control>
														<Switch.Thumb />
													</Switch.Control>
												</Switch>
											) : member.excluded ? (
												t("payroll.page.excluded")
											) : (
												t("common.yes")
											)}
										</Table.Cell>
									</Table.Row>
								))}
							</Table.Body>
						</Table.Content>
					</Table.ScrollContainer>
				</Table>
			)}
		</div>
	);
}
