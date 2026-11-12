import { Button, EmptyState, Spinner, Table } from "@heroui/react";
import { useQuery } from "@tanstack/react-query";
import { getRouteApi, Link as RouterLink } from "@tanstack/react-router";
import { useCallback, useRef } from "react";
import { useTranslation } from "react-i18next";
import { ListSearch } from "#/components/table/ListSearch";
import { Pager } from "#/components/table/Pager";
import { CompanySelect } from "#/features/companies/CompanySelect";
import { EmployeeDrawer, type EmployeeDrawerHandle } from "./EmployeeDrawer";
import { EMPLOYEES_PAGE_SIZE, employeesQuery } from "./employeesQueries";

const route = getRouteApi("/employees/");

export function EmployeesPage() {
	const { t } = useTranslation();
	const search = route.useSearch();
	const navigate = route.useNavigate();
	const drawer = useRef<EmployeeDrawerHandle>(null);
	const { data, isPending } = useQuery(employeesQuery(search));

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
					<h1 className="text-2xl font-semibold">{t("employees.title")}</h1>
					{data ? (
						<p className="text-sm text-muted">{t("employees.total", { count: data.total })}</p>
					) : null}
				</div>
				<Button onPress={() => drawer.current?.create(search.companyId)}>{t("common.add")}</Button>
			</header>

			<div className="flex flex-wrap items-end gap-4">
				<CompanySelect
					className="w-56"
					label={t("employees.fields.company")}
					allLabel={t("employees.allCompanies")}
					value={search.companyId ?? ""}
					onChange={(companyId) =>
						void navigate({
							search: (prev) => ({ ...prev, companyId: companyId || undefined, page: undefined }),
							replace: true,
						})
					}
				/>
				<ListSearch
					value={search.search}
					placeholder={t("employees.searchPlaceholder")}
					onSearch={onSearch}
				/>
			</div>

			{isPending ? (
				<Spinner aria-label={t("common.loading")} />
			) : (
				<Table>
					<Table.ScrollContainer>
						<Table.Content aria-label={t("employees.title")} className="min-w-[760px]">
							<Table.Header>
								<Table.Column isRowHeader>{t("employees.fields.name")}</Table.Column>
								<Table.Column>{t("employees.fields.company")}</Table.Column>
								<Table.Column>{t("employees.fields.number")}</Table.Column>
								<Table.Column>{t("employees.fields.birthDate")}</Table.Column>
								<Table.Column>{t("employees.fields.city")}</Table.Column>
								<Table.Column>{t("employees.fields.account")}</Table.Column>
							</Table.Header>
							<Table.Body
								renderEmptyState={() => (
									<EmptyState className="py-8 text-center text-sm text-muted">
										{t("employees.empty")}
									</EmptyState>
								)}
							>
								{(data?.items ?? []).map((employee) => (
									<Table.Row key={employee.id} id={employee.id}>
										<Table.Cell>
											<RouterLink
												to="/employees/$id"
												params={{ id: employee.id }}
												search={{ tab: undefined }}
												className="font-medium text-accent hover:underline"
											>
												{employee.lastName} {employee.firstName}
											</RouterLink>
										</Table.Cell>
										<Table.Cell>{employee.companyShortName}</Table.Cell>
										<Table.Cell>
											<code>{employee.number}</code>
										</Table.Cell>
										<Table.Cell>{employee.birthDate}</Table.Cell>
										<Table.Cell>{employee.city ?? "—"}</Table.Cell>
										<Table.Cell>{employee.accountLogin ?? "—"}</Table.Cell>
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
					pageSize={EMPLOYEES_PAGE_SIZE}
					onPage={(target) =>
						void navigate({
							search: (prev) => ({ ...prev, page: target === 1 ? undefined : target }),
						})
					}
				/>
			) : null}

			<EmployeeDrawer ref={drawer} />
		</section>
	);
}
