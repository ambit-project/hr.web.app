import { Chip } from "@heroui/react";
import { useTranslation } from "react-i18next";
import type { EmploymentStatus } from "./types";

const COLORS: Record<EmploymentStatus, "success" | "warning" | "default"> = {
	active: "success",
	future: "warning",
	ended: "default",
};

export function EmploymentStatusChip({ status }: { status: EmploymentStatus }) {
	const { t } = useTranslation();
	return (
		<Chip size="sm" color={COLORS[status]}>
			{t(`employments.status.${status}`)}
		</Chip>
	);
}
