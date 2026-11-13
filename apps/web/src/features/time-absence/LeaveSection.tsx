import { Chip, Spinner } from "@heroui/react";
import { useQuery } from "@tanstack/react-query";
import { useRef } from "react";
import { useTranslation } from "react-i18next";
import { AddButton, EditButton } from "#/components/ui/AddButton";
import { problemMessage } from "#/forms/feedback";
import { LeaveDialog, type LeaveDialogHandle } from "./LeaveDialog";
import { Notice } from "./Notice";
import { leaveQuery } from "./timeAbsenceQueries";
import { formatHours, formatNumber, hoursToDays } from "./types";

/**
 * "Urlop wypoczynkowy" on an employment contract: proposal from the Labour Code, saved limit, carried-over leave, used
 * leave and the balance. Without a saved limit the balance is 0 h and leave cannot be recorded, so setting it is the
 * first thing the section offers.
 */
export function LeaveSection({ employmentId, year }: { employmentId: string; year: number }) {
	const { t } = useTranslation();
	const dialog = useRef<LeaveDialogHandle>(null);
	const { data, isPending, error } = useQuery(leaveQuery(employmentId, year));

	const hoursAndDays = (hours: number) =>
		t("timeAbsence.leave.hoursAndDays", {
			hours: formatHours(hours),
			days: formatNumber(hoursToDays(hours)),
		});

	return (
		<div className="flex flex-col gap-3">
			<div className="flex items-center gap-3">
				<h3 className="text-base font-semibold">{t("timeAbsence.leave.title")}</h3>
				{data ? (
					data.saved ? (
						<EditButton
							size="sm"
							label={t("timeAbsence.leave.set.title", { year })}
							onPress={() => dialog.current?.edit(employmentId, year, data)}
						/>
					) : (
						<AddButton
							size="sm"
							label={t("timeAbsence.leave.set.title", { year })}
							onPress={() => dialog.current?.edit(employmentId, year, data)}
						/>
					)
				) : null}
			</div>
			{isPending ? (
				<Spinner aria-label={t("common.loading")} />
			) : error || !data ? (
				<Notice status="danger">{problemMessage(t, error)}</Notice>
			) : (
				<>
					{data.saved ? null : <Notice>{t("timeAbsence.leave.noLimit", { year })}</Notice>}
					{data.carriedOverWarning ? (
						<Notice>{t("timeAbsence.leave.carriedWarning")}</Notice>
					) : null}
					<dl className="grid max-w-2xl grid-cols-[14rem_1fr] gap-x-4 gap-y-1.5 text-sm tabular-nums">
						<dt className="text-muted">{t("timeAbsence.leave.proposal")}</dt>
						<dd>{hoursAndDays(data.proposedHours)}</dd>
						{data.saved ? (
							<>
								<dt className="text-muted">{t("timeAbsence.leave.baseDays")}</dt>
								<dd>
									{t("timeAbsence.leave.daysValue", { days: data.baseDays ?? 0 })}
									{data.firstJob ? `, ${t("timeAbsence.leave.firstJob").toLowerCase()}` : null}
								</dd>
							</>
						) : null}
						<dt className="text-muted">{t("timeAbsence.leave.entitled")}</dt>
						<dd>
							{hoursAndDays(data.entitledHours)}
							{data.entitledOverridden ? <Overridden /> : null}
						</dd>
						<dt className="text-muted">{t("timeAbsence.leave.carried")}</dt>
						<dd>
							{hoursAndDays(data.carriedOverHours)}
							{data.carriedOverOverridden ? <Overridden /> : null}
						</dd>
						{data.overrideReason ? (
							<>
								<dt className="text-muted">{t("timeAbsence.leave.overrideReason")}</dt>
								<dd>{data.overrideReason}</dd>
							</>
						) : null}
						<dt className="text-muted">{t("timeAbsence.leave.used")}</dt>
						<dd>{hoursAndDays(data.usedHours)}</dd>
						<dt className="font-medium">{t("timeAbsence.leave.balance")}</dt>
						<dd className="font-medium">
							{t("timeAbsence.leave.hoursAndDays", {
								hours: formatHours(data.balanceHours),
								days: formatNumber(data.balanceDays),
							})}
						</dd>
					</dl>
				</>
			)}
			<LeaveDialog ref={dialog} />
		</div>
	);
}

function Overridden() {
	const { t } = useTranslation();
	return (
		<Chip size="sm" color="accent" className="ml-2">
			{t("timeAbsence.leave.overridden")}
		</Chip>
	);
}
