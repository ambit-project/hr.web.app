import { useTranslation } from "react-i18next";
import { ConsentsSection } from "./ConsentsSection";
import { GarnishmentsSection } from "./GarnishmentsSection";

/** "Potrącenia" tab of the employee page: written consents and garnishments; the payroll (009) does the maths. */
export function DeductionsSection({ employeeId }: { employeeId: string }) {
	const { t } = useTranslation();
	return (
		<div className="flex flex-col gap-8">
			<p className="max-w-3xl text-sm text-muted">{t("payComponents.deductionsHint")}</p>
			<ConsentsSection employeeId={employeeId} />
			<GarnishmentsSection employeeId={employeeId} />
		</div>
	);
}
