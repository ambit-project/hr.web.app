import { Spinner } from "@heroui/react";
import { useQuery } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import { DeclarationsTable, PpkSummary, RatePeriodsTable } from "./PpkView";
import { myPpkQuery } from "./ppkQueries";

/** The person's own PPK in "Moje dokumenty" (R14, read only): status, rates and declarations. */
export function MyPpk({ employeeId }: { employeeId: string }) {
	const { t } = useTranslation();
	const { data, isPending } = useQuery(myPpkQuery(employeeId));

	if (isPending) return <Spinner aria-label={t("common.loading")} />;
	if (!data) return null;
	return (
		<div className="flex flex-col gap-3">
			<PpkSummary ppk={data} />
			{data.enrolled && data.ratePeriods.length > 0 ? (
				<>
					<h3 className="text-sm font-semibold">{t("ppk.participation.rateHistory")}</h3>
					<RatePeriodsTable items={data.ratePeriods} />
				</>
			) : null}
			<h3 className="text-sm font-semibold">{t("ppk.declarations.title")}</h3>
			<DeclarationsTable items={data.declarations} />
		</div>
	);
}
