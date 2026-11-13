import { Chip, EmptyState, Table } from "@heroui/react";
import type { ReactNode } from "react";
import { useTranslation } from "react-i18next";
import type { ListConsentsItem } from "#/api/models";
import { formatMoney } from "#/lib/money";
import type { ConsentStatus } from "./types";

const COLORS: Record<ConsentStatus, "success" | "warning" | "default" | "danger"> = {
	unsigned: "danger",
	future: "warning",
	active: "success",
	ended: "default",
};

/** Deduction consents of one employee record; `actions` adds a last column (Kadry), self-service shows none. */
export function ConsentsTable({
	items,
	actions,
}: {
	items: readonly ListConsentsItem[];
	actions?: (item: ListConsentsItem) => ReactNode;
}) {
	const { t } = useTranslation();
	return (
		<Table>
			<Table.ScrollContainer>
				<Table.Content aria-label={t("payComponents.consents.title")} className="min-w-[760px]">
					<Table.Header>
						<Table.Column isRowHeader>{t("payComponents.consents.fields.component")}</Table.Column>
						<Table.Column className="text-right">
							{t("payComponents.consents.fields.monthlyAmountShort")}
						</Table.Column>
						<Table.Column>{t("payComponents.consents.fields.validFrom")}</Table.Column>
						<Table.Column>{t("payComponents.consents.fields.validToShort")}</Table.Column>
						<Table.Column>{t("payComponents.consents.fields.signedAtShort")}</Table.Column>
						<Table.Column>{t("payComponents.consents.fields.status")}</Table.Column>
						{actions ? <Table.Column>{t("common.actions")}</Table.Column> : null}
					</Table.Header>
					<Table.Body
						renderEmptyState={() => (
							<EmptyState className="py-8 text-center text-sm text-muted">
								{t("payComponents.consents.empty")}
							</EmptyState>
						)}
					>
						{items.map((item) => (
							<Table.Row key={item.id} id={item.id}>
								<Table.Cell>
									<code>{item.code}</code> {item.name}
								</Table.Cell>
								<Table.Cell className="text-right whitespace-nowrap">
									{formatMoney(item.monthlyAmount)}
								</Table.Cell>
								<Table.Cell>{item.validFrom}</Table.Cell>
								<Table.Cell>{item.validTo ?? "…"}</Table.Cell>
								<Table.Cell>{item.signedAt ?? "—"}</Table.Cell>
								<Table.Cell>
									<Chip size="sm" color={COLORS[item.status as ConsentStatus]}>
										{t(`payComponents.consents.status.${item.status as ConsentStatus}`)}
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
