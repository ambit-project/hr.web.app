import { AlertDialog, Button, EmptyState, Spinner, Table, toast } from "@heroui/react";
import { useQuery } from "@tanstack/react-query";
import { getRouteApi, Link as RouterLink, useNavigate } from "@tanstack/react-router";
import { useRef } from "react";
import { useTranslation } from "react-i18next";
import type { GetEmploymentResponse } from "#/api/models";
import { ProblemError } from "#/lib/problem";
import { EmploymentDialog, type EmploymentDialogHandle } from "./EmploymentDialog";
import { EmploymentStatusChip } from "./EmploymentStatusChip";
import {
	employmentQuery,
	useDeleteEmployment,
	useDeleteLastEmploymentTerms,
} from "./employmentsQueries";
import {
	type ContractType,
	type CostKind,
	type EmploymentKind,
	type EmploymentStatus,
	formatAmount,
	formatFte,
	type Insurance,
	type PayBasis,
} from "./types";

const route = getRouteApi("/employments/$id");

export function EmploymentDetailsPage() {
	const { t } = useTranslation();
	const { id } = route.useParams();
	const { data: employment, isPending } = useQuery(employmentQuery(id));
	const dialog = useRef<EmploymentDialogHandle>(null);

	if (isPending || !employment) {
		return <Spinner aria-label={t("common.loading")} />;
	}

	const kind = employment.kind as EmploymentKind;
	const rows: [string, string | null][] = [
		[t("employments.fields.kind"), t(`employments.kinds.${kind}`)],
		[
			t("employments.fields.contractType"),
			employment.contractType
				? t(`employments.contractTypes.${employment.contractType as ContractType}`)
				: null,
		],
		[t("employments.fields.signedAt"), employment.signedAt],
		[t("employments.fields.startDate"), employment.startDate],
		[t("employments.fields.endDate"), employment.endDate],
		[t("employments.fields.terminatedAt"), employment.terminatedAt],
		[t("employments.retentionUntil"), employment.retentionUntil],
	];

	return (
		<section className="flex flex-col gap-4">
			<RouterLink
				to="/employees/$id"
				params={{ id: employment.employeeId }}
				search={{ tab: "employments" }}
				className="text-sm text-muted hover:underline"
			>
				← {employment.employeeName}
			</RouterLink>
			<header className="flex flex-wrap items-center gap-3">
				<h1 className="text-2xl font-semibold">
					{t("employments.titleOne", { number: employment.number })}
				</h1>
				<EmploymentStatusChip status={employment.status as EmploymentStatus} />
				<p className="w-full text-sm text-muted">{employment.companyShortName}</p>
			</header>

			<dl className="grid max-w-2xl grid-cols-[12rem_1fr] gap-x-4 gap-y-2 text-sm">
				{rows.map(([label, value]) => (
					<div key={label} className="contents">
						<dt className="text-muted">{label}</dt>
						<dd>{value ?? "—"}</dd>
					</div>
				))}
			</dl>
			<div className="flex flex-wrap gap-2">
				<Button onPress={() => dialog.current?.edit(employment)}>{t("common.edit")}</Button>
				<Button variant="secondary" onPress={() => dialog.current?.terminate(employment)}>
					{t("employments.terminate.action")}
				</Button>
				<DeleteEmploymentButton employment={employment} />
			</div>

			<h2 className="pt-4 text-lg font-semibold">{t("employments.terms.history")}</h2>
			<div className="flex gap-2">
				<Button onPress={() => dialog.current?.addTerms(employment)}>
					{t("employments.terms.add")}
				</Button>
				{employment.termsHistory.length > 1 ? (
					<DeleteLastTermsButton employment={employment} />
				) : null}
			</div>
			<Table>
				<Table.ScrollContainer>
					<Table.Content aria-label={t("employments.terms.history")} className="min-w-[900px]">
						<Table.Header>
							<Table.Column isRowHeader>{t("employments.fields.validFrom")}</Table.Column>
							<Table.Column>{t("employments.fields.validTo")}</Table.Column>
							<Table.Column>{t("employments.fields.position")}</Table.Column>
							<Table.Column>{t("employments.fields.pay")}</Table.Column>
							<Table.Column>{t("employments.fields.zusCodes")}</Table.Column>
							<Table.Column>{t("employments.fields.insurances")}</Table.Column>
							<Table.Column>{t("employments.fields.costKind")}</Table.Column>
						</Table.Header>
						<Table.Body
							renderEmptyState={() => (
								<EmptyState className="py-8 text-center text-sm text-muted">—</EmptyState>
							)}
						>
							{employment.termsHistory.map((terms) => (
								<Table.Row key={terms.id} id={terms.id}>
									<Table.Cell>{terms.validFrom}</Table.Cell>
									<Table.Cell>{terms.validTo ?? "…"}</Table.Cell>
									<Table.Cell>
										{terms.positionName
											? `${terms.positionName}, ${formatFte(terms.fteNumerator, terms.fteDenominator)}`
											: "—"}
									</Table.Cell>
									<Table.Cell>
										{formatAmount(terms.baseAmount)} zł
										<span className="block text-xs text-muted">
											{t(`employments.payBases.${terms.payBasis as PayBasis}`)}
										</span>
									</Table.Cell>
									<Table.Cell>
										{terms.zusTitleCode ? (
											<code>
												{terms.zusTitleCode} {terms.zusPensionCode} {terms.zusDisabilityCode}
											</code>
										) : (
											"—"
										)}
									</Table.Cell>
									<Table.Cell>
										{terms.insurances.length > 0
											? terms.insurances
													.map((item) => t(`employments.insurancesShort.${item as Insurance}`))
													.join(", ")
											: t("employments.noInsurances")}
									</Table.Cell>
									<Table.Cell>
										{t(`employments.costKinds.${terms.costKind as CostKind}`)}
									</Table.Cell>
								</Table.Row>
							))}
						</Table.Body>
					</Table.Content>
				</Table.ScrollContainer>
			</Table>
			<EmploymentDialog ref={dialog} />
		</section>
	);
}

function ConfirmButton({
	label,
	title,
	body,
	onConfirm,
}: {
	label: string;
	title: string;
	body: string;
	onConfirm: () => void;
}) {
	const { t } = useTranslation();
	return (
		<AlertDialog>
			<Button variant="danger-soft">{label}</Button>
			<AlertDialog.Backdrop>
				<AlertDialog.Container>
					<AlertDialog.Dialog className="sm:max-w-[420px]">
						<AlertDialog.Header>
							<AlertDialog.Icon status="danger" />
							<AlertDialog.Heading>{title}</AlertDialog.Heading>
						</AlertDialog.Header>
						<AlertDialog.Body>
							<p>{body}</p>
						</AlertDialog.Body>
						<AlertDialog.Footer>
							<Button slot="close" variant="tertiary">
								{t("common.cancel")}
							</Button>
							<Button slot="close" variant="danger" onPress={onConfirm}>
								{label}
							</Button>
						</AlertDialog.Footer>
					</AlertDialog.Dialog>
				</AlertDialog.Container>
			</AlertDialog.Backdrop>
		</AlertDialog>
	);
}

function DeleteEmploymentButton({ employment }: { employment: GetEmploymentResponse }) {
	const { t } = useTranslation();
	const remove = useDeleteEmployment();
	const navigate = useNavigate();

	const onDelete = async () => {
		try {
			await remove.mutateAsync(employment.id);
			toast.success(t("employments.delete.deleted"));
			await navigate({
				to: "/employees/$id",
				params: { id: employment.employeeId },
				search: { tab: "employments" },
			});
		} catch {
			toast.danger(t("common.serverError"));
		}
	};

	return (
		<ConfirmButton
			label={t("employments.delete.action")}
			title={t("employments.delete.title", { number: employment.number })}
			body={t("employments.delete.body")}
			onConfirm={() => void onDelete()}
		/>
	);
}

function DeleteLastTermsButton({ employment }: { employment: GetEmploymentResponse }) {
	const { t } = useTranslation();
	const remove = useDeleteLastEmploymentTerms();

	const onDelete = async () => {
		try {
			await remove.mutateAsync(employment.id);
			toast.success(t("employments.terms.deleted"));
		} catch (error) {
			toast.danger(
				error instanceof ProblemError && error.problem.reason
					? t(`reasons.${error.problem.reason}`, t("common.conflict"))
					: t("common.serverError"),
			);
		}
	};

	return (
		<ConfirmButton
			label={t("employments.terms.deleteLast")}
			title={t("employments.terms.deleteLast")}
			body={t("employments.terms.deleteLastBody")}
			onConfirm={() => void onDelete()}
		/>
	);
}
