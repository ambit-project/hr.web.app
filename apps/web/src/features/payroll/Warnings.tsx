import { Tooltip } from "@heroui/react";
import type { TFunction } from "i18next";
import { TriangleAlert } from "lucide-react";
import { useTranslation } from "react-i18next";
import { Notice } from "#/features/time-absence/Notice";
import type { PayslipWarning } from "./payrollQueries";
import { TraceInline } from "./TraceView";
import { isBlockingWarning, isWarningCode } from "./types";

export function warningText(t: TFunction<"translation">, code: string): string {
	return isWarningCode(code)
		? t(`payroll.warnings.${code}`)
		: t("payroll.warnings.unknown", { code });
}

/** Warning icon in the payslips table; the tooltip lists the warnings. */
export function WarningsIcon({ warnings }: { warnings: readonly PayslipWarning[] }) {
	const { t } = useTranslation();
	const label = warnings.map((warning) => warningText(t, warning.code)).join(" ");
	return (
		<Tooltip delay={200}>
			<Tooltip.Trigger aria-label={label}>
				<TriangleAlert
					className={
						warnings.some((warning) => isBlockingWarning(warning.code))
							? "size-4 text-danger"
							: "size-4 text-warning"
					}
					aria-hidden
				/>
			</Tooltip.Trigger>
			<Tooltip.Content className="max-w-sm">
				<ul className="flex flex-col gap-1">
					{warnings.map((warning, index) => (
						// biome-ignore lint/suspicious/noArrayIndexKey: the same warning can repeat with other details
						<li key={`${warning.code}-${index}`}>{warningText(t, warning.code)}</li>
					))}
				</ul>
			</Tooltip.Content>
		</Tooltip>
	);
}

const noticeStatus = (code: string) => (isBlockingWarning(code) ? "danger" : "warning");

/** Warnings of a payslip as notices, each with its details (amounts, counts) in readable form. */
export function WarningNotices({ warnings }: { warnings: readonly PayslipWarning[] }) {
	const { t } = useTranslation();
	return (
		<div className="flex flex-col gap-2">
			{warnings.map((warning, index) => (
				// biome-ignore lint/suspicious/noArrayIndexKey: the same warning can repeat with other details
				<Notice key={`${warning.code}-${index}`} status={noticeStatus(warning.code)}>
					<span className="font-medium">{warningText(t, warning.code)}</span>
					<TraceInline trace={warning.details} />
				</Notice>
			))}
		</div>
	);
}
