import { EmptyState, Label, ListBox, Select, Spinner, Table } from "@heroui/react";
import { useQuery } from "@tanstack/react-query";
import { getRouteApi } from "@tanstack/react-router";
import { useTranslation } from "react-i18next";
import { Pager } from "#/components/table/Pager";
import { DayInput } from "#/components/ui/DateInputs";
import { AUDIT_ENTITIES, AUDIT_PAGE_SIZE, type AuditEntity, auditQuery } from "./auditQueries";
import { changes, formatValue } from "./changes";

const route = getRouteApi("/audit/");

const ALL = "all";
const timeFormat = new Intl.DateTimeFormat("pl-PL", {
	dateStyle: "short",
	timeStyle: "medium",
	timeZone: "Europe/Warsaw",
});

export function AuditPage() {
	const { t } = useTranslation();
	const search = route.useSearch();
	const navigate = route.useNavigate();
	const { data, isPending } = useQuery(auditQuery(search));

	const filter = (next: Partial<typeof search>) =>
		void navigate({ search: (prev) => ({ ...prev, ...next, page: undefined }), replace: true });

	return (
		<section className="flex flex-col gap-4">
			<header>
				<h1 className="text-2xl font-semibold">{t("audit.title")}</h1>
				<p className="text-sm text-muted">{t("audit.hint")}</p>
			</header>

			<div className="flex flex-wrap items-end gap-4">
				<Select
					className="w-56"
					value={search.entity ?? ALL}
					onChange={(key) =>
						filter({ entity: key === ALL || key === null ? undefined : (key as AuditEntity) })
					}
				>
					<Label>{t("audit.entity")}</Label>
					<Select.Trigger>
						<Select.Value />
						<Select.Indicator />
					</Select.Trigger>
					<Select.Popover>
						<ListBox>
							<ListBox.Item id={ALL} textValue={t("audit.allEntities")}>
								{t("audit.allEntities")}
								<ListBox.ItemIndicator />
							</ListBox.Item>
							{AUDIT_ENTITIES.map((entity) => (
								<ListBox.Item key={entity} id={entity} textValue={t(`audit.entities.${entity}`)}>
									{t(`audit.entities.${entity}`)}
									<ListBox.ItemIndicator />
								</ListBox.Item>
							))}
						</ListBox>
					</Select.Popover>
				</Select>
				<DayInput
					className="w-48"
					label={t("audit.from")}
					value={search.from}
					onChange={(from) => filter({ from: from ?? undefined })}
				/>
				<DayInput
					className="w-48"
					label={t("audit.to")}
					value={search.to}
					onChange={(to) => filter({ to: to ?? undefined })}
				/>
			</div>

			{isPending ? (
				<Spinner aria-label={t("common.loading")} />
			) : (
				<Table>
					<Table.ScrollContainer>
						<Table.Content aria-label={t("audit.title")} className="min-w-[860px]">
							<Table.Header>
								<Table.Column isRowHeader>{t("audit.at")}</Table.Column>
								<Table.Column>{t("audit.actor")}</Table.Column>
								<Table.Column>{t("audit.entity")}</Table.Column>
								<Table.Column>{t("audit.action")}</Table.Column>
								<Table.Column>{t("audit.changes")}</Table.Column>
							</Table.Header>
							<Table.Body
								renderEmptyState={() => (
									<EmptyState className="py-8 text-center text-sm text-muted">
										{t("audit.empty")}
									</EmptyState>
								)}
							>
								{(data?.items ?? []).map((entry) => (
									<Table.Row key={entry.id} id={entry.id}>
										<Table.Cell className="whitespace-nowrap">
											{timeFormat.format(new Date(entry.at))}
										</Table.Cell>
										<Table.Cell>{entry.actorName ?? t("audit.system")}</Table.Cell>
										<Table.Cell>
											{AUDIT_ENTITIES.includes(entry.entity as AuditEntity)
												? t(`audit.entities.${entry.entity as AuditEntity}`)
												: entry.entity}
										</Table.Cell>
										<Table.Cell>
											{t(`audit.actions.${entry.action as "create" | "update" | "delete"}`)}
										</Table.Cell>
										<Table.Cell>
											<ul className="text-sm">
												{changes(entry.before, entry.after).map((change) => (
													<li key={change.field}>
														<code>{change.field}</code>: {formatValue(change.before)} →{" "}
														{formatValue(change.after)}
													</li>
												))}
											</ul>
										</Table.Cell>
									</Table.Row>
								))}
							</Table.Body>
						</Table.Content>
					</Table.ScrollContainer>
				</Table>
			)}

			{data ? (
				<Pager
					page={search.page ?? 1}
					total={data.total}
					pageSize={AUDIT_PAGE_SIZE}
					onPage={(target) =>
						void navigate({
							search: (prev) => ({ ...prev, page: target === 1 ? undefined : target }),
						})
					}
				/>
			) : null}
		</section>
	);
}
