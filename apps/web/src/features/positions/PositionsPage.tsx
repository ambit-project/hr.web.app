import { Button, Chip, EmptyState, Pagination, SearchField, Spinner, Table } from "@heroui/react";
import { useQuery } from "@tanstack/react-query";
import { getRouteApi } from "@tanstack/react-router";
import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { CreatePositionDrawer, type CreatePositionDrawerHandle } from "./CreatePositionDrawer";
import { POSITIONS_PAGE_SIZE, positionsQuery } from "./positionsQueries";

const route = getRouteApi("/positions/");

export function PositionsPage() {
	const { t } = useTranslation();
	const search = route.useSearch();
	const navigate = route.useNavigate();
	const drawer = useRef<CreatePositionDrawerHandle>(null);
	const { data, isPending } = useQuery(positionsQuery(search));
	const [term, setTerm] = useState(search.search ?? "");

	// Typing updates the URL after a pause; a new search always starts from the first page.
	useEffect(() => {
		const handle = setTimeout(() => {
			const next = term.trim() === "" ? undefined : term.trim();
			if (next !== search.search) {
				void navigate({ search: { search: next, page: undefined }, replace: true });
			}
		}, 300);
		return () => clearTimeout(handle);
	}, [term, search.search, navigate]);

	const page = search.page ?? 1;
	const pages = data ? Math.max(1, Math.ceil(data.total / POSITIONS_PAGE_SIZE)) : 1;
	const goTo = (target: number) =>
		navigate({ search: (prev) => ({ ...prev, page: target === 1 ? undefined : target }) });

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

			<SearchField value={term} onChange={setTerm} aria-label={t("common.search")}>
				<SearchField.Group>
					<SearchField.SearchIcon />
					<SearchField.Input
						className="w-full sm:w-80"
						placeholder={t("positions.searchPlaceholder")}
					/>
					<SearchField.ClearButton />
				</SearchField.Group>
			</SearchField>

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

			{pages > 1 ? (
				<Pagination className="justify-center">
					<Pagination.Content>
						<Pagination.Item>
							<Pagination.Previous isDisabled={page <= 1} onPress={() => goTo(page - 1)}>
								<Pagination.PreviousIcon />
								<span>{t("common.previous")}</span>
							</Pagination.Previous>
						</Pagination.Item>
						{Array.from({ length: pages }, (_, index) => index + 1).map((number) => (
							<Pagination.Item key={number}>
								<Pagination.Link isActive={number === page} onPress={() => goTo(number)}>
									{number}
								</Pagination.Link>
							</Pagination.Item>
						))}
						<Pagination.Item>
							<Pagination.Next isDisabled={page >= pages} onPress={() => goTo(page + 1)}>
								<span>{t("common.next")}</span>
								<Pagination.NextIcon />
							</Pagination.Next>
						</Pagination.Item>
					</Pagination.Content>
				</Pagination>
			) : null}

			<CreatePositionDrawer ref={drawer} />
		</section>
	);
}
