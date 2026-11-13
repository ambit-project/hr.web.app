import { Button, Chip, EmptyState, Spinner, Table } from "@heroui/react";
import { useQuery } from "@tanstack/react-query";
import { getRouteApi } from "@tanstack/react-router";
import { useCallback, useRef } from "react";
import { useTranslation } from "react-i18next";
import { ListSearch } from "#/components/table/ListSearch";
import { Pager } from "#/components/table/Pager";
import { hasRole } from "#/features/auth/session";
import { CompanyDialog, type CompanyDialogHandle } from "./CompanyDialog";
import { COMPANIES_PAGE_SIZE, companiesQuery } from "./companiesQueries";

const route = getRouteApi("/companies/");

function formatNip(nip: string) {
	return `${nip.slice(0, 3)}-${nip.slice(3, 6)}-${nip.slice(6, 8)}-${nip.slice(8)}`;
}

export function CompaniesPage() {
	const { t } = useTranslation();
	const { me } = route.useRouteContext();
	const search = route.useSearch();
	const navigate = route.useNavigate();
	const dialog = useRef<CompanyDialogHandle>(null);
	const { data, isPending } = useQuery(companiesQuery(search));
	const canEdit = hasRole(me, "administrator");
	const page = search.page ?? 1;

	const onSearch = useCallback(
		(term: string | undefined) =>
			void navigate({ search: { search: term, page: undefined }, replace: true }),
		[navigate],
	);

	return (
		<section className="flex flex-col gap-4">
			<header className="flex flex-wrap items-center justify-between gap-3">
				<div>
					<h1 className="text-2xl font-semibold">{t("companies.title")}</h1>
					{data ? (
						<p className="text-sm text-muted">{t("companies.total", { count: data.total })}</p>
					) : null}
				</div>
				{canEdit ? (
					<Button onPress={() => dialog.current?.create()}>{t("common.add")}</Button>
				) : null}
			</header>

			<ListSearch
				value={search.search}
				placeholder={t("companies.searchPlaceholder")}
				onSearch={onSearch}
			/>

			{isPending ? (
				<Spinner aria-label={t("common.loading")} />
			) : (
				<Table>
					<Table.ScrollContainer>
						<Table.Content aria-label={t("companies.title")} className="min-w-[720px]">
							<Table.Header>
								<Table.Column isRowHeader>{t("companies.fields.shortName")}</Table.Column>
								<Table.Column>{t("companies.fields.name")}</Table.Column>
								<Table.Column>{t("companies.fields.nip")}</Table.Column>
								<Table.Column>{t("companies.fields.city")}</Table.Column>
								<Table.Column>{t("companies.fields.taxOffice")}</Table.Column>
								<Table.Column>{t("companies.fields.isActive")}</Table.Column>
								<Table.Column>{""}</Table.Column>
							</Table.Header>
							<Table.Body
								renderEmptyState={() => (
									<EmptyState className="py-8 text-center text-sm text-muted">
										{t("companies.empty")}
									</EmptyState>
								)}
							>
								{(data?.items ?? []).map((company) => (
									<Table.Row key={company.id} id={company.id}>
										<Table.Cell>{company.shortName}</Table.Cell>
										<Table.Cell>{company.name}</Table.Cell>
										<Table.Cell>{formatNip(company.nip)}</Table.Cell>
										<Table.Cell>{company.city}</Table.Cell>
										<Table.Cell className="text-sm">
											{company.taxOfficeCode ? (
												<code title={company.taxOfficeName ?? undefined}>
													{company.taxOfficeCode}
												</code>
											) : (
												"—"
											)}
										</Table.Cell>
										<Table.Cell>
											<Chip size="sm" color={company.isActive ? "success" : "default"}>
												{company.isActive ? t("common.yes") : t("common.no")}
											</Chip>
										</Table.Cell>
										<Table.Cell>
											{canEdit ? (
												<Button
													size="sm"
													variant="ghost"
													onPress={() => dialog.current?.edit(company)}
												>
													{t("common.edit")}
												</Button>
											) : null}
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
					page={page}
					total={data.total}
					pageSize={COMPANIES_PAGE_SIZE}
					onPage={(target) =>
						void navigate({
							search: (prev) => ({ ...prev, page: target === 1 ? undefined : target }),
						})
					}
				/>
			) : null}

			<CompanyDialog ref={dialog} />
		</section>
	);
}
