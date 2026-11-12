import { Button, Chip, EmptyState, Spinner, Table } from "@heroui/react";
import { useQuery } from "@tanstack/react-query";
import { getRouteApi } from "@tanstack/react-router";
import { useCallback, useRef } from "react";
import { useTranslation } from "react-i18next";
import { ListSearch } from "#/components/table/ListSearch";
import { Pager } from "#/components/table/Pager";
import { ToggleFilter } from "#/components/table/ToggleFilter";
import { hasRole } from "#/features/auth/session";
import { TaxOfficeDrawer, type TaxOfficeDrawerHandle } from "./TaxOfficeDrawer";
import { TAX_OFFICES_PAGE_SIZE, taxOfficesQuery } from "./taxOfficesQueries";

const route = getRouteApi("/tax-offices/");

export function TaxOfficesPage() {
	const { t } = useTranslation();
	const search = route.useSearch();
	const navigate = route.useNavigate();
	const { me } = route.useRouteContext();
	// Only hides buttons that would end in 403; the database checks the role.
	const canEdit = hasRole(me, "administrator");
	const drawer = useRef<TaxOfficeDrawerHandle>(null);
	const { data, isPending } = useQuery(taxOfficesQuery(search));

	const onSearch = useCallback(
		(term: string | undefined) =>
			void navigate({
				search: (prev) => ({ ...prev, search: term, page: undefined }),
				replace: true,
			}),
		[navigate],
	);

	return (
		<section className="flex flex-col gap-4">
			<header className="flex flex-wrap items-center justify-between gap-3">
				<div>
					<h1 className="text-2xl font-semibold">{t("taxOffices.title")}</h1>
					<p className="text-sm text-muted">{t("taxOffices.hint")}</p>
				</div>
				{canEdit ? (
					<Button onPress={() => drawer.current?.create()}>{t("common.add")}</Button>
				) : null}
			</header>

			<div className="flex flex-wrap items-center gap-4">
				<ListSearch
					value={search.search}
					placeholder={t("taxOffices.searchPlaceholder")}
					onSearch={onSearch}
				/>
				<ToggleFilter
					label={t("taxOffices.showInactive")}
					value={search.inactive ?? false}
					onChange={(inactive) =>
						void navigate({
							search: (prev) => ({ ...prev, inactive: inactive || undefined, page: undefined }),
							replace: true,
						})
					}
				/>
			</div>

			{isPending ? (
				<Spinner aria-label={t("common.loading")} />
			) : (
				<Table>
					<Table.ScrollContainer>
						<Table.Content aria-label={t("taxOffices.title")} className="min-w-[560px]">
							<Table.Header>
								<Table.Column isRowHeader>{t("taxOffices.fields.code")}</Table.Column>
								<Table.Column>{t("taxOffices.fields.name")}</Table.Column>
								<Table.Column>{t("taxOffices.fields.isActive")}</Table.Column>
								<Table.Column>{""}</Table.Column>
							</Table.Header>
							<Table.Body
								renderEmptyState={() => (
									<EmptyState className="py-8 text-center text-sm text-muted">
										{t("taxOffices.empty")}
									</EmptyState>
								)}
							>
								{(data?.items ?? []).map((office) => (
									<Table.Row key={office.id} id={office.id}>
										<Table.Cell>
											<code>{office.code}</code>
										</Table.Cell>
										<Table.Cell>{office.name}</Table.Cell>
										<Table.Cell>
											<Chip size="sm" color={office.isActive ? "success" : "default"}>
												{office.isActive ? t("common.yes") : t("common.no")}
											</Chip>
										</Table.Cell>
										<Table.Cell>
											{canEdit ? (
												<Button
													size="sm"
													variant="ghost"
													onPress={() => drawer.current?.edit(office)}
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
					page={search.page ?? 1}
					total={data.total}
					pageSize={TAX_OFFICES_PAGE_SIZE}
					onPage={(target) =>
						void navigate({
							search: (prev) => ({ ...prev, page: target === 1 ? undefined : target }),
						})
					}
				/>
			) : null}

			{canEdit ? <TaxOfficeDrawer ref={drawer} /> : null}
		</section>
	);
}
