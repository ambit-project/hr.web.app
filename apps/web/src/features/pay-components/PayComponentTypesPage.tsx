import { Chip, EmptyState, Spinner, Table } from "@heroui/react";
import { useQuery } from "@tanstack/react-query";
import { getRouteApi } from "@tanstack/react-router";
import { useRef } from "react";
import { useTranslation } from "react-i18next";
import type { ListComponentTypesItem } from "#/api/models";
import { ToggleFilter } from "#/components/table/ToggleFilter";
import { AddButton, EditButton } from "#/components/ui/AddButton";
import { hasRole } from "#/features/auth/session";
import {
	PayComponentTypeDialog,
	type PayComponentTypeDialogHandle,
} from "./PayComponentTypeDialog";
import { componentTypesQuery } from "./payComponentsQueries";
import { type Calculation, type ComponentKind, type DeductionCategory, setFlags } from "./types";

const route = getRouteApi("/pay-components/");

/** Dictionary of additions and deductions: Kadry read it, the Administrator keeps it. */
export function PayComponentTypesPage() {
	const { t } = useTranslation();
	const search = route.useSearch();
	const navigate = route.useNavigate();
	const { me } = route.useRouteContext();
	// Only hides buttons that would end in 403; the database checks the role.
	const canEdit = hasRole(me, "administrator");
	const dialog = useRef<PayComponentTypeDialogHandle>(null);
	const { data, isPending } = useQuery(componentTypesQuery(search.inactive ?? false));

	return (
		<section className="flex flex-col gap-4">
			<header className="flex flex-wrap items-center justify-between gap-3">
				<div>
					<h1 className="text-2xl font-semibold">{t("payComponents.title")}</h1>
					<p className="max-w-3xl text-sm text-muted">{t("payComponents.hint")}</p>
				</div>
				{canEdit ? (
					<AddButton
						label={t("payComponents.create.title")}
						onPress={() => dialog.current?.create()}
					/>
				) : null}
			</header>

			<ToggleFilter
				label={t("payComponents.showInactive")}
				value={search.inactive ?? false}
				onChange={(inactive) =>
					void navigate({ search: { inactive: inactive || undefined }, replace: true })
				}
			/>

			{isPending ? (
				<Spinner aria-label={t("common.loading")} />
			) : (
				<Table>
					<Table.ScrollContainer>
						<Table.Content aria-label={t("payComponents.title")} className="min-w-[900px]">
							<Table.Header>
								<Table.Column isRowHeader className="w-24">
									{t("payComponents.fields.code")}
								</Table.Column>
								<Table.Column>{t("payComponents.fields.name")}</Table.Column>
								<Table.Column>{t("payComponents.fields.kind")}</Table.Column>
								<Table.Column>{t("payComponents.fields.calculation")}</Table.Column>
								<Table.Column>{t("payComponents.fields.features")}</Table.Column>
								<Table.Column>{t("payComponents.fields.isSystem")}</Table.Column>
								<Table.Column>{t("payComponents.fields.isActive")}</Table.Column>
								{canEdit ? (
									<Table.Column className="w-12">
										<span className="sr-only">{t("common.actions")}</span>
									</Table.Column>
								) : null}
							</Table.Header>
							<Table.Body
								renderEmptyState={() => (
									<EmptyState className="py-8 text-center text-sm text-muted">
										{t("payComponents.empty")}
									</EmptyState>
								)}
							>
								{(data ?? []).map((item) => (
									<Table.Row key={item.id} id={item.id}>
										<Table.Cell>
											<code>{item.code}</code>
										</Table.Cell>
										<Table.Cell>{item.name}</Table.Cell>
										<Table.Cell>
											{t(`payComponents.kinds.${item.kind as ComponentKind}`)}
										</Table.Cell>
										<Table.Cell>
											{t(`payComponents.calculations.${item.calculation as Calculation}`)}
										</Table.Cell>
										<Table.Cell>
											<Features item={item} />
										</Table.Cell>
										<Table.Cell>{item.isSystem ? t("payComponents.yes") : ""}</Table.Cell>
										<Table.Cell>
											<Chip size="sm" color={item.isActive ? "success" : "default"}>
												{item.isActive ? t("common.yes") : t("common.no")}
											</Chip>
										</Table.Cell>
										{canEdit ? (
											<Table.Cell>
												{item.isSystem ? null : (
													<EditButton
														size="sm"
														variant="ghost"
														label={t("payComponents.edit.title", { code: item.code })}
														onPress={() => dialog.current?.edit(item)}
													/>
												)}
											</Table.Cell>
										) : null}
									</Table.Row>
								))}
							</Table.Body>
						</Table.Content>
					</Table.ScrollContainer>
				</Table>
			)}

			<PayComponentTypeDialog ref={dialog} />
		</section>
	);
}

/** Additions: the bases they belong to, as short marks; deductions: the category (and whether a consent is needed). */
function Features({ item }: { item: ListComponentTypesItem }) {
	const { t } = useTranslation();
	if (item.kind === "deduction") {
		return (
			<>
				{item.deductionCategory
					? t(`payComponents.categories.${item.deductionCategory as DeductionCategory}`)
					: "—"}
				{item.requiresConsent ? (
					<span className="block text-xs text-muted">{t("payComponents.requiresConsent")}</span>
				) : null}
			</>
		);
	}
	const flags = setFlags(item);
	return flags.length === 0 ? (
		"—"
	) : (
		<span className="flex flex-wrap gap-x-2">
			{flags.map((flag) => (
				<abbr key={flag} title={t(`payComponents.flags.${flag}`)} className="no-underline">
					✓ {t(`payComponents.flagsShort.${flag}`)}
				</abbr>
			))}
		</span>
	);
}
