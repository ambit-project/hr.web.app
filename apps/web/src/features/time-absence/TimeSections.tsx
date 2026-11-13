import { useTranslation } from "react-i18next";
import { AbsencesSection } from "./AbsencesSection";
import { LeaveSection } from "./LeaveSection";
import { MandateHoursSection } from "./MandateHoursSection";
import { NormSection } from "./NormSection";
import { OvertimeSection } from "./OvertimeSection";
import type { TimeContract } from "./types";
import { YearSelect } from "./YearSelect";

/**
 * "Czas pracy" on the contract page, for one year: absences for every contract that may have them (not specific work);
 * leave, norm and overtime for an employment contract; hours for a mandate paid by the hour.
 */
export function TimeSections({
	contract,
	year,
	onYearChange,
}: {
	contract: TimeContract;
	year: number;
	onYearChange: (year: number) => void;
}) {
	const { t } = useTranslation();
	if (contract.kind === "specific_work") return null;
	const isEmployment = contract.kind === "employment";

	return (
		<div className="flex flex-col gap-6 pt-4">
			<div className="flex flex-wrap items-end justify-between gap-4">
				<div>
					<h2 className="text-lg font-semibold">{t("timeAbsence.section.title")}</h2>
					<p className="text-sm text-muted">{t("timeAbsence.section.hint")}</p>
				</div>
				<YearSelect value={year} onChange={onYearChange} />
			</div>
			{isEmployment ? <LeaveSection employmentId={contract.id} year={year} /> : null}
			<AbsencesSection contract={contract} year={year} />
			{isEmployment ? <NormSection employmentId={contract.id} year={year} /> : null}
			{isEmployment ? <OvertimeSection employmentId={contract.id} year={year} /> : null}
			{contract.kind === "mandate" && contract.hourly ? (
				<MandateHoursSection employmentId={contract.id} year={year} />
			) : null}
		</div>
	);
}
