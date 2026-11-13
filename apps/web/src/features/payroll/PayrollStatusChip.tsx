import { Chip } from "@heroui/react";
import { useTranslation } from "react-i18next";
import { displayStatus, STATUS_COLORS } from "./types";

/** Status of a list; a calculated list whose data changed shows as "nieaktualna". */
export function PayrollStatusChip({ payroll }: { payroll: { status: string; isStale: boolean } }) {
	const { t } = useTranslation();
	const status = displayStatus(payroll);
	return (
		<Chip size="sm" color={STATUS_COLORS[status]}>
			{t(`payroll.statuses.${status}`)}
		</Chip>
	);
}
