import { Button, EmptyState, Spinner, Table } from "@heroui/react";
import { useQuery } from "@tanstack/react-query";
import { useRef } from "react";
import { useTranslation } from "react-i18next";
import type { GetEmployeeResponse } from "#/api/models";
import { AddButton } from "#/components/ui/AddButton";
import { BankAccountDialog, type BankAccountDialogHandle } from "../BankAccountDialog";
import { employeeBankAccountsQuery } from "../employeesQueries";

/** PL61 1090 1014 … — groups of four, as on a bank statement. */
export function formatIban(iban: string) {
	return iban.replace(/(.{4})/g, "$1 ").trim();
}

export function BankSection({ employee }: { employee: GetEmployeeResponse }) {
	const { t } = useTranslation();
	const dialog = useRef<BankAccountDialogHandle>(null);
	const { data, isPending } = useQuery(employeeBankAccountsQuery(employee.id));

	return (
		<div className="flex flex-col gap-4">
			<p className="text-sm">
				{t("employees.bank.current")}:{" "}
				{employee.iban ? <code>{formatIban(employee.iban)}</code> : t("employees.bank.cash")}
			</p>
			<div className="flex gap-2">
				<AddButton label={t("employees.bank.add")} onPress={() => dialog.current?.open("add")} />
				{employee.iban ? (
					<Button variant="secondary" onPress={() => dialog.current?.open("close")}>
						{t("employees.bank.close")}
					</Button>
				) : null}
			</div>
			{isPending ? (
				<Spinner aria-label={t("common.loading")} />
			) : (
				<Table>
					<Table.ScrollContainer>
						<Table.Content aria-label={t("employees.tabs.bank")} className="min-w-[480px]">
							<Table.Header>
								<Table.Column isRowHeader>{t("employees.fields.iban")}</Table.Column>
								<Table.Column>{t("employees.fields.validFrom")}</Table.Column>
								<Table.Column>{t("employees.fields.validTo")}</Table.Column>
							</Table.Header>
							<Table.Body
								renderEmptyState={() => (
									<EmptyState className="py-8 text-center text-sm text-muted">
										{t("employees.bank.empty")}
									</EmptyState>
								)}
							>
								{(data ?? []).map((account) => (
									<Table.Row key={account.id} id={account.id}>
										<Table.Cell>
											<code>•••• {account.last4}</code>
										</Table.Cell>
										<Table.Cell>{account.validFrom}</Table.Cell>
										<Table.Cell>{account.validTo ?? "—"}</Table.Cell>
									</Table.Row>
								))}
							</Table.Body>
						</Table.Content>
					</Table.ScrollContainer>
				</Table>
			)}
			<BankAccountDialog ref={dialog} employeeId={employee.id} />
		</div>
	);
}
