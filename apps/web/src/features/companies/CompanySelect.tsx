import { FieldError, Label, ListBox, Select } from "@heroui/react";
import { useQuery } from "@tanstack/react-query";
import { activeCompaniesQuery } from "./companiesQueries";

const ALL = "all";

type CompanySelectProps = {
	label: string;
	/** Company id; "" = none (or "all" when allLabel is set). */
	value: string;
	onChange: (id: string) => void;
	/** Adds an "all companies" option for list filters. */
	allLabel?: string;
	isRequired?: boolean;
	errorMessage?: string | null;
	className?: string;
};

export function CompanySelect({
	label,
	value,
	onChange,
	allLabel,
	isRequired,
	errorMessage,
	className,
}: CompanySelectProps) {
	const { data } = useQuery(activeCompaniesQuery());

	return (
		<Select
			className={className}
			value={value === "" ? (allLabel ? ALL : null) : value}
			onChange={(key) => onChange(key === null || key === ALL ? "" : String(key))}
			isRequired={isRequired}
			isInvalid={Boolean(errorMessage)}
			validationBehavior="aria"
		>
			<Label>{label}</Label>
			<Select.Trigger>
				<Select.Value />
				<Select.Indicator />
			</Select.Trigger>
			<Select.Popover>
				<ListBox>
					{allLabel ? (
						<ListBox.Item id={ALL} textValue={allLabel}>
							{allLabel}
							<ListBox.ItemIndicator />
						</ListBox.Item>
					) : null}
					{(data?.items ?? []).map((company) => (
						<ListBox.Item key={company.id} id={company.id} textValue={company.shortName}>
							{company.shortName}
							<ListBox.ItemIndicator />
						</ListBox.Item>
					))}
				</ListBox>
			</Select.Popover>
			<FieldError>{errorMessage}</FieldError>
		</Select>
	);
}
