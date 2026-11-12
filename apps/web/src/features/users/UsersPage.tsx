import { Button, Chip, EmptyState, Spinner, Table } from "@heroui/react";
import { useQuery } from "@tanstack/react-query";
import { getRouteApi } from "@tanstack/react-router";
import { useCallback, useRef } from "react";
import { useTranslation } from "react-i18next";
import { ListSearch } from "#/components/table/ListSearch";
import { Pager } from "#/components/table/Pager";
import type { Role } from "#/features/auth/auth.functions";
import { UserDrawer, type UserDrawerHandle } from "./UserDrawer";
import { USERS_PAGE_SIZE, usersQuery } from "./usersQueries";

const route = getRouteApi("/users/");

export function UsersPage() {
	const { t } = useTranslation();
	const search = route.useSearch();
	const navigate = route.useNavigate();
	const drawer = useRef<UserDrawerHandle>(null);
	const { data, isPending } = useQuery(usersQuery(search));

	const onSearch = useCallback(
		(term: string | undefined) =>
			void navigate({ search: { search: term, page: undefined }, replace: true }),
		[navigate],
	);

	return (
		<section className="flex flex-col gap-4">
			<header className="flex flex-wrap items-center justify-between gap-3">
				<h1 className="text-2xl font-semibold">{t("users.title")}</h1>
				<Button onPress={() => drawer.current?.create()}>{t("common.add")}</Button>
			</header>

			<ListSearch
				value={search.search}
				placeholder={t("users.searchPlaceholder")}
				onSearch={onSearch}
			/>

			{isPending ? (
				<Spinner aria-label={t("common.loading")} />
			) : (
				<Table>
					<Table.ScrollContainer>
						<Table.Content aria-label={t("users.title")} className="min-w-[760px]">
							<Table.Header>
								<Table.Column isRowHeader>{t("users.fields.displayName")}</Table.Column>
								<Table.Column>{t("users.fields.login")}</Table.Column>
								<Table.Column>{t("users.fields.email")}</Table.Column>
								<Table.Column>{t("users.fields.roles")}</Table.Column>
								<Table.Column>{""}</Table.Column>
								<Table.Column>{""}</Table.Column>
							</Table.Header>
							<Table.Body
								renderEmptyState={() => (
									<EmptyState className="py-8 text-center text-sm text-muted">
										{t("users.empty")}
									</EmptyState>
								)}
							>
								{(data?.items ?? []).map((user) => (
									<Table.Row key={user.id} id={user.id}>
										<Table.Cell>{user.displayName}</Table.Cell>
										<Table.Cell>
											<code>{user.login}</code>
										</Table.Cell>
										<Table.Cell>{user.email}</Table.Cell>
										<Table.Cell>
											{user.roles.map((role) => t(`roles.${role as Role}`)).join(", ")}
										</Table.Cell>
										<Table.Cell>
											<div className="flex flex-wrap gap-1">
												{!user.isActive ? <Chip size="sm">{t("users.inactive")}</Chip> : null}
												{user.isLocked ? (
													<Chip size="sm" color="danger">
														{t("users.locked")}
													</Chip>
												) : null}
												{user.isDevAccount ? (
													<Chip size="sm" color="warning">
														{t("users.dev")}
													</Chip>
												) : null}
											</div>
										</Table.Cell>
										<Table.Cell>
											<div className="flex gap-1">
												<Button
													size="sm"
													variant="ghost"
													onPress={() => drawer.current?.edit(user)}
												>
													{t("common.edit")}
												</Button>
												<Button
													size="sm"
													variant="ghost"
													onPress={() => drawer.current?.password(user)}
												>
													{t("users.password.action")}
												</Button>
											</div>
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
					pageSize={USERS_PAGE_SIZE}
					onPage={(target) =>
						void navigate({
							search: (prev) => ({ ...prev, page: target === 1 ? undefined : target }),
						})
					}
				/>
			) : null}

			<UserDrawer ref={drawer} />
		</section>
	);
}
