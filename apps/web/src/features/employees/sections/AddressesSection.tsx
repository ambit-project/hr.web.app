import { Button, EmptyState, Spinner, Table } from "@heroui/react";
import { useQuery } from "@tanstack/react-query";
import { useRef } from "react";
import { useTranslation } from "react-i18next";
import { AddressDrawer, type AddressDrawerHandle } from "../AddressDrawer";
import { employeeAddressesQuery } from "../employeesQueries";
import type { AddressKind } from "../types";

export function AddressesSection({ employeeId }: { employeeId: string }) {
	const { t } = useTranslation();
	const drawer = useRef<AddressDrawerHandle>(null);
	const { data, isPending } = useQuery(employeeAddressesQuery(employeeId));

	return (
		<div className="flex flex-col gap-4">
			<div className="flex gap-2">
				<Button onPress={() => drawer.current?.open("residence")}>
					{t("employees.addresses.add")}
				</Button>
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
			<AddressDrawer ref={drawer} employeeId={employeeId} />
		</div>
	);
}
