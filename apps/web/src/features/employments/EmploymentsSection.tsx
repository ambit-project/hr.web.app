import { Button, Chip, EmptyState, Spinner, Table } from "@heroui/react";
import { useQuery } from "@tanstack/react-query";
import { Link as RouterLink } from "@tanstack/react-router";
import { useRef } from "react";
import { useTranslation } from "react-i18next";
import { EmploymentDialog, type EmploymentDialogHandle } from "./EmploymentDialog";
import { EmploymentStatusChip } from "./EmploymentStatusChip";
import { employmentsQuery } from "./employmentsQueries";
import { type ContractType, type EmploymentKind, type EmploymentStatus, formatFte } from "./types";

/** Contracts tab of the employee page: list and the "new contract" dialog. */
export function EmploymentsSection({ employeeId }: { employeeId: string }) {
	const { t } = useTranslation();
	const dialog = useRef<EmploymentDialogHandle>(null);
	const { data, isPending } = useQuery(employmentsQuery(employeeId));

	return (
		<div className="flex flex-col gap-4">
			<div className="flex gap-2">
				<Button onPress={() => dialog.current?.create(employeeId)}>
					{t("employments.create.title")}
				</Button>
			</div>
			{isPending ? (
				<Spinner aria-label={t("common.loading")} />
			) : (
				<Table>
					<Table.ScrollContainer>
						<Table.Content aria-label={t("employees.tabs.employments")} className="min-w-[860px]">
							<Table.Header>
								<Table.Column isRowHeader>{t("employments.fields.number")}</Table.Column>
								<Table.Column>{t("employments.fields.kind")}</Table.Column>
								<Table.Column>{t("employments.period")}</Table.Column>
								<Table.Column>{t("employments.fields.position")}</Table.Column>
								<Table.Column>{t("employments.status.label")}</Table.Column>
								<Table.Column>{t("employments.retentionUntil")}</Table.Column>
							</Table.Header>
							<Table.Body
								renderEmptyState={() => (
									<EmptyState className="py-8 text-center text-sm text-muted">
										{t("employments.empty")}
									</EmptyState>
								)}
							>
								{(data ?? []).map((item) => (
									<Table.Row key={item.id} id={item.id}>
										<Table.Cell>
											<RouterLink
												to="/employments/$id"
												params={{ id: item.id }}
												className="text-accent hover:underline"
											>
												{item.number}
											</RouterLink>
										</Table.Cell>
										<Table.Cell>
											{t(`employments.kinds.${item.kind as EmploymentKind}`)}
											{item.contractType ? (
												<span className="block text-xs text-muted">
													{t(`employments.contractTypes.${item.contractType as ContractType}`)}
												</span>
											) : null}
										</Table.Cell>
										<Table.Cell>
											{item.startDate} – {item.terminatedAt ?? item.endDate ?? "…"}
											{item.terminatedAt ? (
												<Chip size="sm" className="ml-2">
													{t("employments.terminatedChip")}
												</Chip>
											) : null}
										</Table.Cell>
										<Table.Cell>
											{item.positionName
												? `${item.positionName}, ${formatFte(item.fteNumerator, item.fteDenominator)}`
												: "—"}
										</Table.Cell>
										<Table.Cell>
											<EmploymentStatusChip status={item.status as EmploymentStatus} />
										</Table.Cell>
										<Table.Cell>{item.retentionUntil ?? "—"}</Table.Cell>
									</Table.Row>
								))}
							</Table.Body>
						</Table.Content>
					</Table.ScrollContainer>
				</Table>
			)}
			<EmploymentDialog ref={dialog} />
		</div>
	);
}
