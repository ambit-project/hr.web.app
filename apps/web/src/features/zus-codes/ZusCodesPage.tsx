import { Button, EmptyState, Label, ListBox, Select, Spinner, Table } from "@heroui/react";
import { useQuery } from "@tanstack/react-query";
import { getRouteApi } from "@tanstack/react-router";
import { useCallback, useRef } from "react";
import { useTranslation } from "react-i18next";
import { ListSearch } from "#/components/table/ListSearch";
import { Pager } from "#/components/table/Pager";
import { DayInput } from "#/components/ui/DateInputs";
import { hasRole } from "#/features/auth/session";
import { ZUS_CODE_KINDS, type ZusCodeKind } from "./types";
import { ZusCodeDialog, type ZusCodeDialogHandle } from "./ZusCodeDialog";
import { ZUS_CODES_PAGE_SIZE, zusCodesQuery } from "./zusCodesQueries";

const route = getRouteApi("/zus-codes/");
const ALL = "all";

export function ZusCodesPage() {
	const { t } = useTranslation();
	const search = route.useSearch();
	const navigate = route.useNavigate();
	const { me } = route.useRouteContext();
	// Only hides buttons that would end in 403; the database checks the role.
	const canEdit = hasRole(me, "administrator");
	const dialog = useRef<ZusCodeDialogHandle>(null);
	const { data, isPending } = useQuery(zusCodesQuery(search));

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
					<h1 className="text-2xl font-semibold">{t("zusCodes.title")}</h1>
					<p className="text-sm text-muted">{t("zusCodes.hint")}</p>
				</div>
				{canEdit ? (
					<Button onPress={() => dialog.current?.create(search.kind)}>{t("common.add")}</Button>
				) : null}
			</header>

			<div className="flex flex-wrap items-end gap-4">
				<Select
					className="w-64"
					value={search.kind ?? ALL}
					onChange={(key) =>
						void navigate({
							search: (prev) => ({
								...prev,
								kind: key === ALL || key === null ? undefined : (key as ZusCodeKind),
								page: undefined,
							}),
							replace: true,
						})
					}
				>
					<Label>{t("zusCodes.kind")}</Label>
					<Select.Trigger>
						<Select.Value />
						<Select.Indicator />
					</Select.Trigger>
					<Select.Popover>
						<ListBox>
							<ListBox.Item id={ALL} textValue={t("zusCodes.allKinds")}>
								{t("zusCodes.allKinds")}
								<ListBox.ItemIndicator />
							</ListBox.Item>
							{ZUS_CODE_KINDS.map((kind) => (
								<ListBox.Item key={kind} id={kind} textValue={t(`zusCodes.kinds.${kind}`)}>
									{t(`zusCodes.kinds.${kind}`)}
									<ListBox.ItemIndicator />
								</ListBox.Item>
							))}
						</ListBox>
					</Select.Popover>
				</Select>
				<ListSearch
					value={search.search}
					placeholder={t("zusCodes.searchPlaceholder")}
					onSearch={onSearch}
				/>
				<DayInput
					className="w-56"
					label={t("zusCodes.onDate")}
					value={search.onDate}
					onChange={(onDate) =>
						void navigate({
							search: (prev) => ({ ...prev, onDate: onDate ?? undefined, page: undefined }),
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
						<Table.Content aria-label={t("zusCodes.title")} className="min-w-[760px]">
							<Table.Header>
								<Table.Column isRowHeader>{t("zusCodes.fields.code")}</Table.Column>
								<Table.Column>{t("zusCodes.kind")}</Table.Column>
								<Table.Column>{t("zusCodes.fields.name")}</Table.Column>
								<Table.Column>{t("zusCodes.fields.validFrom")}</Table.Column>
								<Table.Column>{t("zusCodes.fields.validTo")}</Table.Column>
								<Table.Column>{""}</Table.Column>
							</Table.Header>
							<Table.Body
								renderEmptyState={() => (
									<EmptyState className="py-8 text-center text-sm text-muted">
										{t("zusCodes.empty")}
									</EmptyState>
								)}
							>
								{(data?.items ?? []).map((code) => (
									<Table.Row key={code.id} id={code.id}>
										<Table.Cell>
											<code>{code.code}</code>
										</Table.Cell>
										<Table.Cell className="text-sm">
											{t(`zusCodes.kinds.${code.kind as ZusCodeKind}`)}
										</Table.Cell>
										<Table.Cell className="max-w-xl text-sm">{code.name}</Table.Cell>
										<Table.Cell>{code.validFrom}</Table.Cell>
										<Table.Cell>{code.validTo ?? "—"}</Table.Cell>
										<Table.Cell>
											{canEdit ? (
												<Button
													size="sm"
													variant="ghost"
													onPress={() => dialog.current?.edit(code)}
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
					pageSize={ZUS_CODES_PAGE_SIZE}
					onPage={(target) =>
						void navigate({
							search: (prev) => ({ ...prev, page: target === 1 ? undefined : target }),
						})
					}
				/>
			) : null}

			{canEdit ? <ZusCodeDialog ref={dialog} /> : null}
		</section>
	);
}
