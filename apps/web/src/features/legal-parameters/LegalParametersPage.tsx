import { EmptyState, Spinner, Table } from "@heroui/react";
import { useQuery } from "@tanstack/react-query";
import { getRouteApi } from "@tanstack/react-router";
import { useCallback, useRef } from "react";
import { useTranslation } from "react-i18next";
import { ListSearch } from "#/components/table/ListSearch";
import { Pager } from "#/components/table/Pager";
import { AddButton } from "#/components/ui/AddButton";
import { DayInput } from "#/components/ui/DateInputs";
import {
	AddLegalParameterDialog,
	type AddLegalParameterDialogHandle,
} from "./AddLegalParameterDialog";
import { LEGAL_PARAMETERS_PAGE_SIZE, legalParametersQuery } from "./legalParametersQueries";

const route = getRouteApi("/legal-parameters/");

// Up to six decimals: rates are stored as fractions (0.0976), amounts with grosze.
const numberFormat = new Intl.NumberFormat("pl-PL", { maximumFractionDigits: 6 });

export function LegalParametersPage() {
	const { t } = useTranslation();
	const search = route.useSearch();
	const navigate = route.useNavigate();
	const dialog = useRef<AddLegalParameterDialogHandle>(null);
	const { data, isPending } = useQuery(legalParametersQuery(search));

	const onSearch = useCallback(
		(code: string | undefined) =>
			void navigate({ search: (prev) => ({ ...prev, code, page: undefined }), replace: true }),
		[navigate],
	);

	return (
		<section className="flex flex-col gap-4">
			<header className="flex flex-wrap items-center justify-between gap-3">
				<div>
					<h1 className="text-2xl font-semibold">{t("legalParameters.title")}</h1>
					<p className="text-sm text-muted">{t("legalParameters.hint")}</p>
				</div>
				<AddButton label={t("legalParameters.add.title")} onPress={() => dialog.current?.open()} />
			</header>

			<div className="flex flex-wrap items-end gap-4">
				<ListSearch
					value={search.code}
					placeholder={t("legalParameters.searchPlaceholder")}
					onSearch={onSearch}
				/>
				<DayInput
					className="w-56"
					label={t("legalParameters.onDate")}
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
						<Table.Content aria-label={t("legalParameters.title")} className="min-w-[760px]">
							<Table.Header>
								<Table.Column isRowHeader>{t("legalParameters.fields.code")}</Table.Column>
								<Table.Column>{t("legalParameters.fields.value")}</Table.Column>
								<Table.Column>{t("legalParameters.fields.validFrom")}</Table.Column>
								<Table.Column>{t("legalParameters.fields.validTo")}</Table.Column>
								<Table.Column>{t("legalParameters.fields.source")}</Table.Column>
								<Table.Column>{""}</Table.Column>
							</Table.Header>
							<Table.Body
								renderEmptyState={() => (
									<EmptyState className="py-8 text-center text-sm text-muted">
										{t("legalParameters.empty")}
									</EmptyState>
								)}
							>
								{(data?.items ?? []).map((parameter) => (
									<Table.Row key={parameter.id} id={parameter.id}>
										<Table.Cell>
											<code>{parameter.code}</code>
										</Table.Cell>
										<Table.Cell>{numberFormat.format(parameter.value)}</Table.Cell>
										<Table.Cell>{parameter.validFrom}</Table.Cell>
										<Table.Cell>{parameter.validTo ?? "—"}</Table.Cell>
										<Table.Cell className="max-w-md text-sm text-muted">
											{parameter.source}
										</Table.Cell>
										<Table.Cell>
											<AddButton
												size="sm"
												variant="ghost"
												label={t("legalParameters.add.title")}
												onPress={() => dialog.current?.open(parameter.code)}
											/>
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
					pageSize={LEGAL_PARAMETERS_PAGE_SIZE}
					onPage={(target) =>
						void navigate({
							search: (prev) => ({ ...prev, page: target === 1 ? undefined : target }),
						})
					}
				/>
			) : null}

			<AddLegalParameterDialog ref={dialog} />
		</section>
	);
}
