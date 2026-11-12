import { Button, Chip, EmptyState, Spinner, Table } from "@heroui/react";
import { useQuery } from "@tanstack/react-query";
import { getRouteApi } from "@tanstack/react-router";
import { useCallback, useRef } from "react";
import { useTranslation } from "react-i18next";
import { ListSearch } from "#/components/table/ListSearch";
import { Pager } from "#/components/table/Pager";
import { CreatePositionDrawer, type CreatePositionDrawerHandle } from "./CreatePositionDrawer";
import { POSITIONS_PAGE_SIZE, positionsQuery } from "./positionsQueries";

const route = getRouteApi("/positions/");

export function PositionsPage() {
	const { t } = useTranslation();
	const search = route.useSearch();
	const navigate = route.useNavigate();
	const drawer = useRef<CreatePositionDrawerHandle>(null);
	const { data, isPending } = useQuery(positionsQuery(search));

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
					<h1 className="text-2xl font-semibold">{t("positions.title")}</h1>
					{data ? (
						<p className="text-sm text-muted">{t("positions.total", { count: data.total })}</p>
					) : null}
				</div>
				<Button onPress={() => drawer.current?.open()}>{t("common.add")}</Button>
			</header>

			<ListSearch
				value={search.search}
				placeholder={t("positions.searchPlaceholder")}
				onSearch={onSearch}
			/>

			{isPending ? (
				<Spinner aria-label={t("common.loading")} />
			) : (
				<Table>
					<Table.ScrollContainer>
						<Table.Content aria-label={t("positions.title")} className="min-w-[560px]">
							<Table.Header>
								<Table.Column isRowHeader>{t("positions.columns.name")}</Table.Column>
								<Table.Column>{t("positions.columns.description")}</Table.Column>
								<Table.Column>{t("positions.columns.active")}</Table.Column>
							</Table.Header>
							<Table.Body
								renderEmptyState={() => (
									<EmptyState className="py-8 text-center text-sm text-muted">
										{t("positions.empty")}
									</EmptyState>
								)}
							>
								{(data?.items ?? []).map((position) => (
									<Table.Row key={position.id} id={position.id}>
										<Table.Cell>{position.name}</Table.Cell>
										<Table.Cell>{position.description ?? ""}</Table.Cell>
										<Table.Cell>
											<Chip size="sm" color={position.isActive ? "success" : "default"}>
												{position.isActive ? t("positions.active.yes") : t("positions.active.no")}
											</Chip>
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
					pageSize={POSITIONS_PAGE_SIZE}
					onPage={(target) =>
						void navigate({
							search: (prev) => ({ ...prev, page: target === 1 ? undefined : target }),
						})
					}
				/>
			) : null}

			<CreatePositionDrawer ref={drawer} />
		</section>
	);
}
