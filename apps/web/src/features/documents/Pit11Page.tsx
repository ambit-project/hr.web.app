import { Chip, EmptyState, Spinner, Table, toast } from "@heroui/react";
import { useQuery } from "@tanstack/react-query";
import { getRouteApi, Link as RouterLink } from "@tanstack/react-router";
import type { TFunction } from "i18next";
import { FileCode2, FilePlus2, HandHelping, RefreshCw, Send } from "lucide-react";
import { useRef } from "react";
import { useTranslation } from "react-i18next";
import type { ListPit11Item } from "#/api/models";
import { IconButton } from "#/components/ui/AddButton";
import { OpenIcon, openLinkClass } from "#/components/ui/OpenLink";
import { CompanySelect } from "#/features/companies/CompanySelect";
import { activeCompaniesQuery } from "#/features/companies/companiesQueries";
import { Notice } from "#/features/time-absence/Notice";
import { currentYear } from "#/features/time-absence/types";
import { YearSelect } from "#/features/time-absence/YearSelect";
import { onActionError, problemMessage } from "#/forms/feedback";
import { saveFile } from "#/lib/download";
import { fetchTaxFormXml, pit11Query, useGeneratePit11 } from "./documentsQueries";
import { TaxFormMarkDialog, type TaxFormMarkDialogHandle } from "./TaxFormMarkDialog";
import {
	type GivenHow,
	isMissingField,
	type Pit11State,
	pit11Deadlines,
	pit11State,
	type TaxFormPurpose,
	xmlFileName,
} from "./types";

const route = getRouteApi("/pit11/");

const STATE_COLORS: Record<Pit11State, "default" | "accent" | "success" | "warning" | "danger"> = {
	notGenerated: "default",
	missingData: "danger",
	generated: "accent",
	given: "warning",
	sent: "success",
};

function missingText(t: TFunction<"translation">, missing: readonly string[]): string {
	return t("documents.pit11.missingList", {
		fields: missing
			.map((field) => (isMissingField(field) ? t(`documents.pit11.missing.${field}`) : field))
			.join(", "),
	});
}

/** "Płace → PIT-11" (Kadry, Administrator): forms of a company for a tax year, generation, marks and XML (R7–R9). */
export function Pit11Page() {
	const { t } = useTranslation();
	const search = route.useSearch();
	const navigate = route.useNavigate();
	const { data: companies } = useQuery(activeCompaniesQuery());
	const companyId = search.companyId ?? companies?.items[0]?.id ?? "";
	const year = search.year ?? currentYear();
	const deadlines = pit11Deadlines(year);

	return (
		<section className="flex flex-col gap-4">
			<header>
				<h1 className="text-2xl font-semibold">{t("documents.pit11.title")}</h1>
				<p className="max-w-3xl text-sm text-muted">{t("documents.pit11.hint")}</p>
			</header>
			<div className="flex flex-wrap items-end gap-3">
				<CompanySelect
					className="w-56"
					label={t("documents.company")}
					value={companyId}
					onChange={(id) =>
						void navigate({
							search: (prev) => ({ ...prev, companyId: id || undefined }),
							replace: true,
						})
					}
				/>
				<YearSelect
					value={year}
					onChange={(value) =>
						void navigate({ search: (prev) => ({ ...prev, year: value }), replace: true })
					}
				/>
			</div>
			<Notice status="accent">
				{t("documents.pit11.deadlines", {
					year,
					office: deadlines.office,
					taxpayer: deadlines.taxpayer,
				})}
			</Notice>
			{companyId ? (
				<Pit11List companyId={companyId} year={year} />
			) : (
				<p className="text-sm text-muted">{t("documents.chooseCompany")}</p>
			)}
		</section>
	);
}

function Pit11List({ companyId, year }: { companyId: string; year: number }) {
	const { t } = useTranslation();
	const dialog = useRef<TaxFormMarkDialogHandle>(null);
	const { data, isPending, error } = useQuery(pit11Query(companyId, year));
	const generate = useGeneratePit11();

	const onGenerate = async (employeeId: string | null) => {
		try {
			const { changed } = await generate.mutateAsync({ companyId, year, employeeId });
			if (changed === 0) toast.info(t("documents.pit11.unchanged"));
			else toast.success(t("documents.pit11.generated", { count: changed }));
		} catch (failure) {
			onActionError(t, failure);
		}
	};

	const onDownload = async (item: ListPit11Item) => {
		if (!item.taxFormId || item.version === null) return;
		try {
			const name = xmlFileName({
				year,
				employeeNumber: item.employeeNumber,
				version: item.version,
			});
			saveFile(name, await fetchTaxFormXml(item.taxFormId), "application/xml");
			toast.success(t("documents.pit11.downloaded", { file: name }));
		} catch (failure) {
			onActionError(t, failure);
		}
	};

	return (
		<div className="flex flex-col gap-3">
			<div className="flex gap-2">
				<IconButton
					icon={FilePlus2}
					variant="primary"
					label={t("documents.pit11.generateAll")}
					isDisabled={generate.isPending}
					onPress={() => void onGenerate(null)}
				/>
			</div>
			{error ? <Notice status="danger">{problemMessage(t, error)}</Notice> : null}
			{isPending ? (
				<Spinner aria-label={t("common.loading")} />
			) : (
				<Table>
					<Table.ScrollContainer>
						<Table.Content aria-label={t("documents.pit11.title")} className="min-w-[1000px]">
							<Table.Header>
								<Table.Column isRowHeader>{t("documents.pit11.person")}</Table.Column>
								<Table.Column>{t("documents.taxForms.version")}</Table.Column>
								<Table.Column>{t("documents.taxForms.generatedAt")}</Table.Column>
								<Table.Column>{t("documents.pit11.status")}</Table.Column>
								<Table.Column>{t("documents.taxForms.sentAt")}</Table.Column>
								<Table.Column>{t("documents.taxForms.givenAt")}</Table.Column>
								<Table.Column>{t("common.actions")}</Table.Column>
								<Table.Column className="w-12">
									<span className="sr-only">{t("common.open")}</span>
								</Table.Column>
							</Table.Header>
							<Table.Body
								renderEmptyState={() => (
									<EmptyState className="py-8 text-center text-sm text-muted">
										{t("documents.pit11.empty")}
									</EmptyState>
								)}
							>
								{(data ?? []).map((item) => {
									const state = pit11State(item);
									const name = `${item.lastName} ${item.firstName}`;
									const complete = item.taxFormId !== null && item.missing.length === 0;
									return (
										<Table.Row key={item.employeeId} id={item.employeeId}>
											<Table.Cell>
												{name}
												<span className="ml-2 text-xs text-muted">{item.employeeNumber}</span>
											</Table.Cell>
											<Table.Cell>
												{item.version === null ? (
													"—"
												) : (
													<>
														{item.version}
														<span className="ml-1 text-xs text-muted">
															{t(`documents.taxForms.purposes.${item.purpose as TaxFormPurpose}`)}
														</span>
													</>
												)}
											</Table.Cell>
											<Table.Cell>{item.generatedAt?.slice(0, 10) ?? "—"}</Table.Cell>
											<Table.Cell>
												<div className="flex flex-wrap items-center gap-1">
													<Chip size="sm" color={STATE_COLORS[state]}>
														{t(`documents.pit11.states.${state}`)}
													</Chip>
													{item.isOutdated ? (
														<Chip size="sm" color="warning">
															{t("documents.pit11.outdated")}
														</Chip>
													) : null}
												</div>
												{item.missing.length > 0 ? (
													<span className="block text-xs text-danger">
														{missingText(t, item.missing)}
													</span>
												) : null}
											</Table.Cell>
											<Table.Cell>{item.sentToOfficeAt ?? "—"}</Table.Cell>
											<Table.Cell>
												{item.givenToEmployeeAt ? (
													<>
														{item.givenToEmployeeAt}
														<span className="ml-1 text-xs text-muted">
															{t(`documents.taxForms.givenHows.${item.givenHow as GivenHow}`)}
														</span>
													</>
												) : (
													"—"
												)}
											</Table.Cell>
											<Table.Cell>
												<div className="flex items-center gap-1">
													<IconButton
														icon={RefreshCw}
														size="sm"
														variant="ghost"
														label={t("documents.pit11.generateOne", { name })}
														isDisabled={generate.isPending}
														onPress={() => void onGenerate(item.employeeId)}
													/>
													{complete ? (
														<>
															<IconButton
																icon={FileCode2}
																size="sm"
																variant="ghost"
																label={t("documents.pit11.downloadXml")}
																onPress={() => void onDownload(item)}
															/>
															{item.sentToOfficeAt === null ? (
																<IconButton
																	icon={Send}
																	size="sm"
																	variant="ghost"
																	label={t("documents.pit11.markSent")}
																	onPress={() => dialog.current?.sent(item.taxFormId ?? "", name)}
																/>
															) : null}
															<IconButton
																icon={HandHelping}
																size="sm"
																variant="ghost"
																label={t("documents.pit11.markGiven")}
																onPress={() =>
																	dialog.current?.given(
																		item.taxFormId ?? "",
																		name,
																		item.givenToEmployeeAt,
																		item.givenHow,
																	)
																}
															/>
														</>
													) : null}
												</div>
											</Table.Cell>
											<Table.Cell>
												{item.taxFormId ? (
													<RouterLink
														to="/tax-forms/$id"
														params={{ id: item.taxFormId }}
														className={openLinkClass}
														aria-label={t("common.open")}
														title={t("common.open")}
													>
														<OpenIcon />
													</RouterLink>
												) : null}
											</Table.Cell>
										</Table.Row>
									);
								})}
							</Table.Body>
						</Table.Content>
					</Table.ScrollContainer>
				</Table>
			)}
			<TaxFormMarkDialog ref={dialog} />
		</div>
	);
}
