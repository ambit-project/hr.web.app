import { EmptyState, Spinner, Table } from "@heroui/react";
import { useQuery } from "@tanstack/react-query";
import { useRef } from "react";
import { useTranslation } from "react-i18next";
import { AddButton } from "#/components/ui/AddButton";
import { AddressDialog, type AddressDialogHandle } from "../AddressDialog";
import { employeeAddressesQuery } from "../employeesQueries";
import type { AddressKind } from "../types";

export function AddressesSection({ employeeId }: { employeeId: string }) {
	const { t } = useTranslation();
	const dialog = useRef<AddressDialogHandle>(null);
	const { data, isPending } = useQuery(employeeAddressesQuery(employeeId));

	return (
		<div className="flex flex-col gap-4">
			<div className="flex gap-2">
				<AddButton
					label={t("employees.addresses.add")}
					onPress={() => dialog.current?.open("residence")}
				/>
			</div>
			{isPending ? (
				<Spinner aria-label={t("common.loading")} />
			) : (
				<Table>
					<Table.ScrollContainer>
						<Table.Content aria-label={t("employees.tabs.addresses")} className="min-w-[640px]">
							<Table.Header>
								<Table.Column isRowHeader>{t("employees.addresses.kind")}</Table.Column>
								<Table.Column>{t("employees.fields.street")}</Table.Column>
								<Table.Column>{t("employees.fields.city")}</Table.Column>
								<Table.Column>{t("employees.fields.validFrom")}</Table.Column>
								<Table.Column>{t("employees.fields.validTo")}</Table.Column>
							</Table.Header>
							<Table.Body
								renderEmptyState={() => (
									<EmptyState className="py-8 text-center text-sm text-muted">
										{t("employees.addresses.empty")}
									</EmptyState>
								)}
							>
								{(data ?? []).map((address) => (
									<Table.Row key={address.id} id={address.id}>
										<Table.Cell>
											{t(`employees.addresses.${address.kind as AddressKind}`)}
										</Table.Cell>
										<Table.Cell>{address.street}</Table.Cell>
										<Table.Cell>
											{address.postalCode} {address.city}
										</Table.Cell>
										<Table.Cell>{address.validFrom}</Table.Cell>
										<Table.Cell>{address.validTo ?? "—"}</Table.Cell>
									</Table.Row>
								))}
							</Table.Body>
						</Table.Content>
					</Table.ScrollContainer>
				</Table>
			)}
			<AddressDialog ref={dialog} employeeId={employeeId} />
		</div>
	);
}
