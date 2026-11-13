import { useTranslation } from "react-i18next";
import { OptionSelect } from "#/components/ui/OptionSelect";
import { currentYear, yearOptions } from "./types";

export function YearSelect({
	value,
	onChange,
	className = "w-32",
}: {
	value: number;
	onChange: (year: number) => void;
	className?: string;
}) {
	const { t } = useTranslation();
	return (
		<OptionSelect
			className={className}
			label={t("timeAbsence.year")}
			value={String(value)}
			options={yearOptions(currentYear(), value).map((year) => ({
				id: String(year),
				label: String(year),
			}))}
			onChange={(year) => year && onChange(Number(year))}
		/>
	);
}
