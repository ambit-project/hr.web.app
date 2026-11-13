import { Checkbox, Chip, EmptyState, Spinner, Table, toast } from "@heroui/react";
import { useQuery } from "@tanstack/react-query";
import { getRouteApi, Link as RouterLink } from "@tanstack/react-router";
import { Copy, FileSpreadsheet } from "lucide-react";
import { useCallback, useRef, useState } from "react";
import type { Selection } from "react-aria-components";
import { useTranslation } from "react-i18next";
import { ListSearch } from "#/components/table/ListSearch";
import { Pager } from "#/components/table/Pager";
import { AddButton, IconButton } from "#/components/ui/AddButton";
import { OpenIcon, openLinkClass } from "#/components/ui/OpenLink";
import { OptionSelect } from "#/components/ui/OptionSelect";
import { CompanySelect } from "#/features/companies/CompanySelect";
import { onActionError } from "#/forms/feedback";
import { birthDateWithAge } from "#/lib/age";
import { saveBase64File } from "#/lib/download";
import { unwrap } from "#/lib/problem";
import { CopyEmployeesDialog, type CopyEmployeesDialogHandle } from "./CopyEmployeesDialog";
import { EmployeeDialog, type EmployeeDialogHandle } from "./EmployeeDialog";
import { exportEmployeesToOptimaFn } from "./employees.functions";
import { EMPLOYEES_PAGE_SIZE, employeesQuery } from "./employeesQueries";
import { EMPLOYEE_STATUSES, type EmployeeStatus } from "./types";

const STATUS_COLORS: Record<EmployeeStatus, "success" | "warning" | "default"> = {
	active: "success",
	future: "warning",
	former: "default",
	none: "default",
};

const route = getRouteApi("/employees/");

export function EmployeesPage() {
	const { t } = useTranslation();
	const search = route.useSearch();
	const navigate = route.useNavigate();
	const dialog = useRef<EmployeeDialogHandle>(null);
	const { data, isPending } = useQuery(employeesQuery(search));
	const copyDialog = useRef<CopyEmployeesDialogHandle>(null);
	const [selection, setSelection] = useState<Selection>(new Set());
	// Selection belongs to the rows on screen: a new filter or page starts with nothing selected (reset during render,
	// React's pattern for state derived from props).
	const searchKey = JSON.stringify(search);
	const [selectionFor, setSelectionFor] = useState(searchKey);
	if (selectionFor !== searchKey) {
		setSelectionFor(searchKey);
		setSelection(new Set());
	}
	const selectedIds =
		selection === "all" ? (data?.items ?? []).map((item) => item.id) : [...selection].map(String);

	const [exporting, setExporting] = useState(false);
	const onOptimaExport = async () => {
		if (!search.companyId) return;
		setExporting(true);
		try {
			const name = `optima-kadry-${new Date().toISOString().slice(0, 10)}.xls`;
			const file = unwrap(
				await exportEmployeesToOptimaFn({
					data: {
						companyId: search.companyId,
						employeeIds: selectedIds,
						search: search.search ?? null,
						status: search.status ?? null,
					},
				}),
			);
			saveBase64File(name, file, "application/vnd.ms-excel");
			toast.success(t("employees.optimaExport.done", { file: name }));
		} catch (failure) {
			onActionError(t, failure);
		} finally {
			setExporting(false);
		}
	};

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
				<div className="flex gap-2">
					<IconButton
						icon={Copy}
						label={t("employees.copy.action")}
						onPress={() =>
							copyDialog.current?.open({
								selectedIds,
								filter: {
									companyId: search.companyId,
									search: search.search,
									status: search.status,
								},
								filterTotal: data?.total ?? 0,
							})
						}
					/>
					<IconButton
						icon={FileSpreadsheet}
						label={
							!search.companyId
								? t("employees.optimaExport.chooseCompany")
								: selectedIds.length > 0
									? t("employees.optimaExport.actionSelected")
									: t("employees.optimaExport.action")
						}
						isDisabled={!search.companyId || exporting}
						onPress={() => void onOptimaExport()}
					/>
					<AddButton
						label={t("employees.create.title")}
						onPress={() => dialog.current?.create(search.companyId)}
					/>
				</div>
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
				<OptionSelect
					className="w-48"
					label={t("employees.status.label")}
					allLabel={t("employees.status.all")}
					value={search.status ?? ""}
					options={EMPLOYEE_STATUSES.map((status) => ({
						id: status,
						label: t(`employees.status.${status}`),
					}))}
					onChange={(status) =>
						void navigate({
							search: (prev) => ({
								...prev,
								status: (status || undefined) as EmployeeStatus | undefined,
								page: undefined,
							}),
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
						<Table.Content
							aria-label={t("employees.title")}
							className="min-w-[760px]"
							selectionMode="multiple"
							selectionBehavior="toggle"
							selectedKeys={selection}
							onSelectionChange={setSelection}
						>
							<Table.Header>
								<Table.Column className="w-8">
									<Checkbox aria-label={t("employees.selectAll")} slot="selection">
										<Checkbox.Content>
											<Checkbox.Control>
												<Checkbox.Indicator />
											</Checkbox.Control>
										</Checkbox.Content>
									</Checkbox>
								</Table.Column>
								<Table.Column isRowHeader>{t("employees.fields.name")}</Table.Column>
								<Table.Column>{t("employees.fields.company")}</Table.Column>
								<Table.Column className="w-16">
									<span title={t("employees.fields.number")}>
										{t("employees.fields.numberShort")}
									</span>
								</Table.Column>
								<Table.Column>{t("employees.fields.acronym")}</Table.Column>
								<Table.Column>{t("employees.fields.pesel")}</Table.Column>
								<Table.Column>{t("employees.fields.birthDate")}</Table.Column>
								<Table.Column>{t("employees.fields.city")}</Table.Column>
								<Table.Column>{t("employees.fields.account")}</Table.Column>
								<Table.Column>{t("employees.status.label")}</Table.Column>
								<Table.Column className="w-12">
									<span className="sr-only">{t("common.open")}</span>
								</Table.Column>
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
											<Checkbox
												aria-label={`${employee.lastName} ${employee.firstName}`}
												slot="selection"
											>
												<Checkbox.Content>
													<Checkbox.Control>
														<Checkbox.Indicator />
													</Checkbox.Control>
												</Checkbox.Content>
											</Checkbox>
										</Table.Cell>
										<Table.Cell>
											<RouterLink
												to="/employees/$id"
												params={{ id: employee.id }}
												search={{ tab: undefined }}
												className="font-medium text-link hover:underline"
											>
												{employee.lastName} {employee.firstName}
											</RouterLink>
										</Table.Cell>
										<Table.Cell>{employee.companyShortName}</Table.Cell>
										<Table.Cell>
											<code>{employee.number}</code>
										</Table.Cell>
										<Table.Cell>
											<code>{employee.acronym}</code>
										</Table.Cell>
										<Table.Cell>
											{employee.peselMasked ? <code>{employee.peselMasked}</code> : "—"}
										</Table.Cell>
										<Table.Cell>{birthDateWithAge(employee.birthDate)}</Table.Cell>
										<Table.Cell>{employee.city ?? "—"}</Table.Cell>
										<Table.Cell>{employee.accountLogin ?? "—"}</Table.Cell>
										<Table.Cell>
											<Chip size="sm" color={STATUS_COLORS[employee.status as EmployeeStatus]}>
												{t(`employees.status.${employee.status as EmployeeStatus}`)}
											</Chip>
										</Table.Cell>
										<Table.Cell>
											<RouterLink
												to="/employees/$id"
												params={{ id: employee.id }}
												search={{ tab: undefined }}
												className={openLinkClass}
												aria-label={t("common.open")}
												title={t("common.open")}
											>
												<OpenIcon />
											</RouterLink>
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
					pageSize={EMPLOYEES_PAGE_SIZE}
					onPage={(target) =>
						void navigate({
							search: (prev) => ({ ...prev, page: target === 1 ? undefined : target }),
						})
					}
				/>
			) : null}

			<EmployeeDialog ref={dialog} />
			<CopyEmployeesDialog ref={copyDialog} />
		</section>
	);
}
