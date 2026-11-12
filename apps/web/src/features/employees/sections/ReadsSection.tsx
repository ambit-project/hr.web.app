import { EmptyState, Spinner, Table } from "@heroui/react";
import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import { Pager } from "#/components/table/Pager";
import { employeeReadAuditQuery, READ_AUDIT_PAGE_SIZE } from "../employeesQueries";

const dateTime = new Intl.DateTimeFormat("pl-PL", { dateStyle: "short", timeStyle: "medium" });

export function ReadsSection({ employeeId }: { employeeId: string }) {
	const { t } = useTranslation();
	const [page, setPage] = useState(1);
	const { data, isPending } = useQuery(employeeReadAuditQuery(employeeId, page));

	return (
		<div className="flex flex-col gap-4">
			<p className="text-sm text-muted">{t("employees.reads.hint")}</p>
			{isPending ? (
				<Spinner aria-label={t("common.loading")} />
			) : (
				<Table>
					<Table.ScrollContainer>
						<Table.Content aria-label={t("employees.tabs.reads")} className="min-w-[480px]">
							<Table.Header>
								<Table.Column isRowHeader>{t("employees.reads.at")}</Table.Column>
								<Table.Column>{t("employees.reads.actor")}</Table.Column>
							</Table.Header>
							<Table.Body
								renderEmptyState={() => (
									<EmptyState className="py-8 text-center text-sm text-muted">
										{t("employees.reads.empty")}
									</EmptyState>
								)}
							>
								{(data?.items ?? []).map((entry) => (
									<Table.Row key={entry.id} id={String(entry.id)}>
										<Table.Cell>{dateTime.format(new Date(entry.at))}</Table.Cell>
										<Table.Cell>{entry.actorName}</Table.Cell>
									</Table.Row>
								))}
							</Table.Body>
						</Table.Content>
					</Table.ScrollContainer>
				</Table>
			)}
			{data ? (
				<Pager page={page} total={data.total} pageSize={READ_AUDIT_PAGE_SIZE} onPage={setPage} />
			) : null}
		</div>
	);
}
