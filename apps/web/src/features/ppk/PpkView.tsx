import { Chip, EmptyState, Table } from "@heroui/react";
import type { TFunction } from "i18next";
import type { ReactNode } from "react";
import { useTranslation } from "react-i18next";
import type {
	GetEmployeePpkDeclaration,
	GetEmployeePpkRatePeriod,
	GetEmployeePpkResponse,
} from "#/api/models";
import {
	type AnyDeclarationKind,
	CANDIDATE_COLORS,
	type CandidateStatus,
	formatRate,
	PARTICIPATION_COLORS,
	type ParticipationStatus,
	statusColor,
} from "./types";

export function PpkStatusChip({ status }: { status: string }) {
	const { t } = useTranslation();
	return (
		<Chip size="sm" color={statusColor(PARTICIPATION_COLORS, status)}>
			{t(`ppk.statuses.${status as ParticipationStatus}`, { defaultValue: status })}
		</Chip>
	);
}

export function CandidateStatusChip({ status }: { status: string }) {
	const { t } = useTranslation();
	return (
		<Chip size="sm" color={statusColor(CANDIDATE_COLORS, status)}>
			{t(`ppk.candidateStatuses.${status as CandidateStatus}`, { defaultValue: status })}
		</Chip>
	);
}

/** Status, enrolment and today's four rates of one employee record (Kadry and "Moje dokumenty"). */
export function PpkSummary({ ppk }: { ppk: GetEmployeePpkResponse }) {
	const { t } = useTranslation();
	const rows: [string, ReactNode][] = [
		[t("ppk.participation.status"), <PpkStatusChip key="status" status={ppk.status} />],
		[t("ppk.participation.enrolledAt"), ppk.enrolledAt ?? "—"],
		[t("ppk.participation.memberNumber"), ppk.institutionMemberNumber ?? "—"],
	];
	const rates: [string, number][] = [
		[t("ppk.participation.employeeBasic"), ppk.employeeBasicRate],
		[t("ppk.participation.employeeAdditional"), ppk.employeeAdditionalRate],
		[t("ppk.participation.employerBasic"), ppk.employerBasicRate],
		[t("ppk.participation.employerAdditional"), ppk.employerAdditionalRate],
	];
	return (
		<div className="flex flex-col gap-3">
			<dl className="grid max-w-2xl grid-cols-[14rem_1fr] gap-x-4 gap-y-2 text-sm">
				{rows.map(([label, value]) => (
					<div key={label} className="contents">
						<dt className="text-muted">{label}</dt>
						<dd>{value}</dd>
					</div>
				))}
			</dl>
			{ppk.enrolled ? (
				<div className="flex flex-col gap-1">
					<h4 className="text-sm font-semibold">{t("ppk.participation.ratesToday")}</h4>
					<dl className="grid max-w-2xl grid-cols-[14rem_1fr] gap-x-4 gap-y-1 text-sm tabular-nums">
						{rates.map(([label, rate]) => (
							<div key={label} className="contents">
								<dt className="text-muted">{label}</dt>
								<dd>{formatRate(rate)}</dd>
							</div>
						))}
					</dl>
				</div>
			) : null}
		</div>
	);
}

export function RatePeriodsTable({ items }: { items: readonly GetEmployeePpkRatePeriod[] }) {
	const { t } = useTranslation();
	return (
		<Table className="max-w-3xl">
			<Table.ScrollContainer>
				<Table.Content aria-label={t("ppk.participation.rateHistory")} className="min-w-[520px]">
					<Table.Header>
						<Table.Column isRowHeader>{t("ppk.participation.validFrom")}</Table.Column>
						<Table.Column>{t("ppk.participation.validTo")}</Table.Column>
						<Table.Column className="text-right">
							{t("ppk.participation.employeeBasic")}
						</Table.Column>
						<Table.Column className="text-right">
							{t("ppk.participation.employeeAdditional")}
						</Table.Column>
					</Table.Header>
					<Table.Body
						renderEmptyState={() => (
							<EmptyState className="py-6 text-center text-sm text-muted">
								{t("ppk.participation.rateHistoryEmpty")}
							</EmptyState>
						)}
					>
						{items.map((item) => (
							<Table.Row key={item.validFrom} id={item.validFrom}>
								<Table.Cell>{item.validFrom}</Table.Cell>
								<Table.Cell>{item.validTo ?? "—"}</Table.Cell>
								<Table.Cell className="text-right">{formatRate(item.employeeBasicRate)}</Table.Cell>
								<Table.Cell className="text-right">
									{formatRate(item.employeeAdditionalRate)}
								</Table.Cell>
							</Table.Row>
						))}
					</Table.Body>
				</Table.Content>
			</Table.ScrollContainer>
		</Table>
	);
}

/** Declarations of one record; `actions` adds a last column (Kadry), self-service shows none. */
export function DeclarationsTable({
	items,
	actions,
}: {
	items: readonly GetEmployeePpkDeclaration[];
	actions?: (item: GetEmployeePpkDeclaration) => ReactNode;
}) {
	const { t } = useTranslation();
	return (
		<Table className="max-w-4xl">
			<Table.ScrollContainer>
				<Table.Content aria-label={t("ppk.declarations.title")} className="min-w-[620px]">
					<Table.Header>
						<Table.Column isRowHeader>{t("ppk.declarations.kind")}</Table.Column>
						<Table.Column>{t("ppk.declarations.submittedAt")}</Table.Column>
						<Table.Column>{t("ppk.declarations.effectiveFrom")}</Table.Column>
						<Table.Column className="text-right">{t("ppk.declarations.rate")}</Table.Column>
						{actions ? <Table.Column>{t("common.actions")}</Table.Column> : null}
					</Table.Header>
					<Table.Body
						renderEmptyState={() => (
							<EmptyState className="py-6 text-center text-sm text-muted">
								{t("ppk.declarations.empty")}
							</EmptyState>
						)}
					>
						{items.map((item) => (
							<Table.Row key={item.id} id={item.id}>
								<Table.Cell>{declarationKindLabel(t, item)}</Table.Cell>
								<Table.Cell>{item.submittedAt}</Table.Cell>
								<Table.Cell>{item.effectiveFrom}</Table.Cell>
								<Table.Cell className="text-right">
									{item.rate === null ? "—" : formatRate(item.rate)}
								</Table.Cell>
								{actions ? <Table.Cell>{actions(item)}</Table.Cell> : null}
							</Table.Row>
						))}
					</Table.Body>
				</Table.Content>
			</Table.ScrollContainer>
		</Table>
	);
}

export function declarationKindLabel(
	t: TFunction<"translation">,
	item: GetEmployeePpkDeclaration,
): string {
	return t(`ppk.declarations.kinds.${item.kind as AnyDeclarationKind}`, {
		year: item.cycleYear ?? "",
		defaultValue: item.kind,
	});
}
