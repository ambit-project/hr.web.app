import { Chip, EmptyState, Table } from "@heroui/react";
import type { ReactNode } from "react";
import { useTranslation } from "react-i18next";
import type { ListTaxDeclarationsItem } from "#/api/models";
import { type DeclarationKind, type DeclarationStatus, formatMonth } from "./types";

const COLORS: Record<DeclarationStatus, "success" | "warning" | "default" | "danger"> = {
	active: "success",
	future: "warning",
	ended: "default",
	lapsed: "default",
};

/** Declarations of one employee record; `actions` adds a last column (Kadry), self-service shows none. */
export function TaxDeclarationsTable({
	items,
	actions,
}: {
	items: readonly ListTaxDeclarationsItem[];
	actions?: (item: ListTaxDeclarationsItem) => ReactNode;
}) {
	const { t } = useTranslation();
	return (
		<Table>
			<Table.ScrollContainer>
				<Table.Content aria-label={t("taxDeclarations.title")} className="min-w-[760px]">
					<Table.Header>
						<Table.Column isRowHeader>{t("taxDeclarations.fields.kind")}</Table.Column>
						<Table.Column>{t("taxDeclarations.fields.submittedAt")}</Table.Column>
						<Table.Column>{t("taxDeclarations.period")}</Table.Column>
						<Table.Column>{t("taxDeclarations.status.label")}</Table.Column>
						{actions ? <Table.Column>{t("common.actions")}</Table.Column> : null}
					</Table.Header>
					<Table.Body
						renderEmptyState={() => (
							<EmptyState className="py-8 text-center text-sm text-muted">
								{t("taxDeclarations.empty")}
							</EmptyState>
						)}
					>
						{items.map((item) => (
							<Table.Row key={item.id} id={item.id}>
								<Table.Cell>
									{t(`taxDeclarations.kinds.${item.kind as DeclarationKind}`)}
									<span className="block text-xs text-muted">{details(t, item)}</span>
								</Table.Cell>
								<Table.Cell>
									{item.submittedAt}
									{item.withdrawnAt ? (
										<span className="block text-xs text-muted">
											{t("taxDeclarations.withdrawnAt", { date: item.withdrawnAt })}
										</span>
									) : null}
								</Table.Cell>
								<Table.Cell>
									{formatMonth(item.validFrom)} – {item.validTo ? formatMonth(item.validTo) : "…"}
									{item.lapsesOn ? (
										<span className="block text-xs text-muted">
											{t("taxDeclarations.lapsesOn", { date: item.lapsesOn })}
										</span>
									) : null}
								</Table.Cell>
								<Table.Cell>
									<Chip size="sm" color={COLORS[item.status as DeclarationStatus]}>
										{t(`taxDeclarations.status.${item.status as DeclarationStatus}`)}
									</Chip>
								</Table.Cell>
								{actions ? <Table.Cell>{actions(item)}</Table.Cell> : null}
							</Table.Row>
						))}
					</Table.Body>
				</Table.Content>
			</Table.ScrollContainer>
		</Table>
	);
}

type TranslateFn = ReturnType<typeof useTranslation>["t"];

function details(t: TranslateFn, item: ListTaxDeclarationsItem): string {
	if (item.share) return t("taxDeclarations.shareValue", { share: item.share });
	if (item.yearFrom !== null && item.yearTo !== null)
		return t("taxDeclarations.yearsValue", { from: item.yearFrom, to: item.yearTo });
	return t(`taxDeclarations.basis.${item.kind as DeclarationKind}`);
}
