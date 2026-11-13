import { Button, EmptyState, Spinner, Table } from "@heroui/react";
import { useQuery } from "@tanstack/react-query";
import { Link as RouterLink } from "@tanstack/react-router";
import { useRef } from "react";
import { useTranslation } from "react-i18next";
import { DayInput } from "#/components/ui/DateInputs";
import { OpenIcon, openLinkClass } from "#/components/ui/OpenLink";
import { Notice } from "#/features/time-absence/Notice";
import { problemMessage } from "#/forms/feedback";
import { EnrollDialog, type EnrollDialogHandle } from "./EnrollDialog";
import { CandidateStatusChip } from "./PpkView";
import { ppkCandidatesQuery } from "./ppkQueries";
import { countByStatus } from "./types";

/** "Do zapisu": who has to be enrolled and by when on a day (R3–R5). */
export function CandidatesTab({
	companyId,
	on,
	onDateChange,
}: {
	companyId: string;
	on: string;
	onDateChange: (on: string | undefined) => void;
}) {
	const { t } = useTranslation();
	const dialog = useRef<EnrollDialogHandle>(null);
	const { data, isPending, error } = useQuery(ppkCandidatesQuery(companyId, on));
	const counts = countByStatus(data ?? []);

	return (
		<div className="flex flex-col gap-3">
			<p className="max-w-4xl text-sm text-muted">{t("ppk.candidates.hint")}</p>
			<DayInput
				className="w-48"
				label={t("ppk.candidates.on")}
				value={on}
				onChange={(value) => onDateChange(value ?? undefined)}
			/>
			{isPending ? (
				<Spinner aria-label={t("common.loading")} />
			) : error || !data ? (
				<Notice status="danger">{problemMessage(t, error)}</Notice>
			) : (
				<>
					{counts.overdue > 0 ? (
						<Notice status="danger">{t("ppk.candidates.overdueHint")}</Notice>
					) : null}
					{counts.requestOnly > 0 ? <Notice>{t("ppk.candidates.requestOnlyHint")}</Notice> : null}
					<p className="text-sm tabular-nums">{t("ppk.candidates.summary", counts)}</p>
					<Table className="max-w-6xl">
						<Table.ScrollContainer>
							<Table.Content aria-label={t("ppk.tabs.candidates")} className="min-w-[900px]">
								<Table.Header>
									<Table.Column isRowHeader>{t("ppk.employee")}</Table.Column>
									<Table.Column>{t("employees.fields.number")}</Table.Column>
									<Table.Column className="text-right">{t("ppk.age")}</Table.Column>
									<Table.Column>{t("ppk.candidates.reachedOn")}</Table.Column>
									<Table.Column>{t("ppk.candidates.deadline")}</Table.Column>
									<Table.Column>{t("ppk.participation.status")}</Table.Column>
									<Table.Column>{t("ppk.candidates.request")}</Table.Column>
									<Table.Column>{t("common.actions")}</Table.Column>
									<Table.Column className="w-10">
										<span className="sr-only">{t("common.open")}</span>
									</Table.Column>
								</Table.Header>
								<Table.Body
									renderEmptyState={() => (
										<EmptyState className="py-8 text-center text-sm text-muted">
											{t("ppk.candidates.empty")}
										</EmptyState>
									)}
								>
									{data.map((item) => {
										const name = `${item.lastName} ${item.firstName}`;
										const overdue = item.status === "overdue";
										return (
											<Table.Row key={item.employeeId} id={item.employeeId}>
												<Table.Cell className={overdue ? "font-semibold text-danger" : undefined}>
													{name}
												</Table.Cell>
												<Table.Cell>{item.employeeNumber}</Table.Cell>
												<Table.Cell className="text-right">{item.age}</Table.Cell>
												<Table.Cell>{item.reachedOn}</Table.Cell>
												<Table.Cell className={overdue ? "font-semibold text-danger" : undefined}>
													{item.deadline}
												</Table.Cell>
												<Table.Cell>
													<CandidateStatusChip status={item.status} />
												</Table.Cell>
												<Table.Cell>
													{item.enrollmentRequest ? t("common.yes") : t("common.no")}
												</Table.Cell>
												<Table.Cell>
													{item.status === "waiting" ? null : (
														<Button
															size="sm"
															variant="secondary"
															onPress={() =>
																dialog.current?.enroll({
																	employeeId: item.employeeId,
																	name,
																	deadline: item.deadline,
																	requestOnly: item.status === "requestOnly",
																})
															}
														>
															{t("ppk.candidates.enroll")}
														</Button>
													)}
												</Table.Cell>
												<Table.Cell>
													<RouterLink
														to="/employees/$id"
														params={{ id: item.employeeId }}
														search={{ tab: "ppk" }}
														className={openLinkClass}
														aria-label={t("common.open")}
														title={t("common.open")}
													>
														<OpenIcon />
													</RouterLink>
												</Table.Cell>
											</Table.Row>
										);
									})}
								</Table.Body>
							</Table.Content>
						</Table.ScrollContainer>
					</Table>
				</>
			)}
			<EnrollDialog ref={dialog} />
		</div>
	);
}
