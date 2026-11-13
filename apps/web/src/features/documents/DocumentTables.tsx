import { EmptyState, Spinner, Table } from "@heroui/react";
import { useQuery } from "@tanstack/react-query";
import { Link as RouterLink, useNavigate } from "@tanstack/react-router";
import { FileSpreadsheet } from "lucide-react";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import { IconButton } from "#/components/ui/AddButton";
import { OpenIcon, openLinkClass } from "#/components/ui/OpenLink";
import { formatPeriod } from "#/features/payroll/types";
import { Notice } from "#/features/time-absence/Notice";
import { currentYear } from "#/features/time-absence/types";
import { YearSelect } from "#/features/time-absence/YearSelect";
import { problemMessage } from "#/forms/feedback";
import { formatDecimal } from "#/lib/money";
import { payslipsQuery, taxFormsQuery } from "./documentsQueries";
import { PayslipPdfButton } from "./PayslipPdfButton";
import type { GivenHow, TaxFormPurpose } from "./types";

/** Payslips of an employee record by payment date; each row opens the printable payslip. */
export function PayslipsTable({
	employeeId,
	mine,
	empty,
}: {
	employeeId: string;
	mine: boolean;
	empty: string;
}) {
	const { t } = useTranslation();
	const { data, isPending, error } = useQuery(payslipsQuery(employeeId, mine));
	if (error) return <Notice status="danger">{problemMessage(t, error)}</Notice>;
	if (isPending) return <Spinner aria-label={t("common.loading")} />;

	return (
		<Table>
			<Table.ScrollContainer>
				<Table.Content aria-label={t("documents.payslips.title")} className="min-w-[720px]">
					<Table.Header>
						<Table.Column isRowHeader>{t("documents.payslips.paymentDate")}</Table.Column>
						<Table.Column>{t("documents.payslips.period")}</Table.Column>
						<Table.Column>{t("documents.payslips.payroll")}</Table.Column>
						<Table.Column>{t("documents.payslips.contract")}</Table.Column>
						<Table.Column className="text-right">{t("documents.payslips.gross")}</Table.Column>
						<Table.Column className="text-right">{t("documents.payslips.net")}</Table.Column>
						<Table.Column className="text-right">{t("documents.payslips.payout")}</Table.Column>
						<Table.Column className="w-12">
							<span className="sr-only">{t("documents.downloadPdf")}</span>
						</Table.Column>
						<Table.Column className="w-12">
							<span className="sr-only">{t("common.open")}</span>
						</Table.Column>
					</Table.Header>
					<Table.Body
						renderEmptyState={() => (
							<EmptyState className="py-6 text-center text-sm text-muted">{empty}</EmptyState>
						)}
					>
						{(data ?? []).map((item) => (
							<Table.Row key={item.id} id={item.id}>
								<Table.Cell>{item.paymentDate}</Table.Cell>
								<Table.Cell>{formatPeriod(item.periodFrom)}</Table.Cell>
								<Table.Cell>{item.payrollNumber}</Table.Cell>
								<Table.Cell>{item.employmentNumber}</Table.Cell>
								<Table.Cell className="text-right">{formatDecimal(item.gross)}</Table.Cell>
								<Table.Cell className="text-right">{formatDecimal(item.net)}</Table.Cell>
								<Table.Cell className="text-right">{formatDecimal(item.payout)}</Table.Cell>
								<Table.Cell>
									<PayslipPdfButton
										entryId={item.id}
										payrollNumber={item.payrollNumber}
										paymentDate={item.paymentDate}
										variant="ghost"
										size="sm"
									/>
								</Table.Cell>
								<Table.Cell>
									<RouterLink
										to="/payslips/$entryId"
										params={{ entryId: item.id }}
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
	);
}

/** PIT-11 versions of an employee record; each row opens the printable form. */
export function TaxFormsTable({
	employeeId,
	mine,
	empty,
}: {
	employeeId: string;
	mine: boolean;
	empty: string;
}) {
	const { t } = useTranslation();
	const { data, isPending, error } = useQuery(taxFormsQuery(employeeId, mine));
	if (error) return <Notice status="danger">{problemMessage(t, error)}</Notice>;
	if (isPending) return <Spinner aria-label={t("common.loading")} />;

	return (
		<Table>
			<Table.ScrollContainer>
				<Table.Content aria-label={t("documents.taxForms.title")} className="min-w-[640px]">
					<Table.Header>
						<Table.Column isRowHeader>{t("documents.taxForms.year")}</Table.Column>
						<Table.Column>{t("documents.taxForms.version")}</Table.Column>
						<Table.Column>{t("documents.taxForms.purpose")}</Table.Column>
						<Table.Column>{t("documents.taxForms.generatedAt")}</Table.Column>
						<Table.Column>{t("documents.taxForms.sentAt")}</Table.Column>
						<Table.Column>{t("documents.taxForms.givenAt")}</Table.Column>
						<Table.Column className="w-12">
							<span className="sr-only">{t("common.open")}</span>
						</Table.Column>
					</Table.Header>
					<Table.Body
						renderEmptyState={() => (
							<EmptyState className="py-6 text-center text-sm text-muted">{empty}</EmptyState>
						)}
					>
						{(data ?? []).map((item) => (
							<Table.Row key={item.id} id={item.id}>
								<Table.Cell>{item.year}</Table.Cell>
								<Table.Cell>{item.version}</Table.Cell>
								<Table.Cell>
									{t(`documents.taxForms.purposes.${item.purpose as TaxFormPurpose}`)}
								</Table.Cell>
								<Table.Cell>{item.generatedAt.slice(0, 10)}</Table.Cell>
								<Table.Cell>{item.sentToOfficeAt ?? "—"}</Table.Cell>
								<Table.Cell>
									{item.givenToEmployeeAt
										? `${item.givenToEmployeeAt} · ${t(`documents.taxForms.givenHows.${item.givenHow as GivenHow}`)}`
										: "—"}
								</Table.Cell>
								<Table.Cell>
									<RouterLink
										to="/tax-forms/$id"
										params={{ id: item.id }}
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
	);
}

/** "Dokumenty" tab of the employee page (Kadry, Administrator): payslips, wage card and PIT-11. */
export function EmployeeDocumentsSection({ employeeId }: { employeeId: string }) {
	const { t } = useTranslation();
	const navigate = useNavigate();
	const [year, setYear] = useState(currentYear());

	return (
		<div className="flex flex-col gap-4">
			<h2 className="text-lg font-semibold">{t("documents.payslips.title")}</h2>
			<PayslipsTable employeeId={employeeId} mine={false} empty={t("documents.payslips.empty")} />
			<h2 className="pt-2 text-lg font-semibold">{t("documents.wageCard.open")}</h2>
			<div className="flex items-end gap-2">
				<YearSelect value={year} onChange={setYear} />
				<IconButton
					icon={FileSpreadsheet}
					label={t("documents.wageCard.title", { year })}
					onPress={() =>
						void navigate({
							to: "/wage-cards/$employeeId",
							params: { employeeId },
							search: { year },
						})
					}
				/>
			</div>
			<h2 className="pt-2 text-lg font-semibold">{t("documents.taxForms.title")}</h2>
			<TaxFormsTable employeeId={employeeId} mine={false} empty={t("documents.taxForms.empty")} />
		</div>
	);
}
