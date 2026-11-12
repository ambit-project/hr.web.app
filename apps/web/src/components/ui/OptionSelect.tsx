import { FieldError, Label, ListBox, Select } from "@heroui/react";

export type Option = { id: string; label: string };

type OptionSelectProps = {
	label: string;
	/** Selected option id; "" = none (or the "all" option when allLabel is set). */
	value: string;
	options: readonly Option[];
	onChange: (id: string) => void;
	onBlur?: () => void;
	/** Adds an option meaning "no filter" for list filters. */
	allLabel?: string;
	isRequired?: boolean;
	isDisabled?: boolean;
	errorMessage?: string | null;
	className?: string;
};

const ALL = "__all";

/** Select over a fixed list of options with the value as a plain string, for forms and list filters. */
export function OptionSelect({
	label,
	value,
	options,
	onChange,
	onBlur,
	allLabel,
	isRequired,
	isDisabled,
	errorMessage,
	className,
}: OptionSelectProps) {
	return (
		<Select
			className={className}
			value={value === "" ? (allLabel ? ALL : null) : value}
			onChange={(key) => onChange(key === null || key === ALL ? "" : String(key))}
			onBlur={onBlur}
			isRequired={isRequired}
			isDisabled={isDisabled}
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
					{options.map((option) => (
						<ListBox.Item key={option.id} id={option.id} textValue={option.label}>
							{option.label}
							<ListBox.ItemIndicator />
						</ListBox.Item>
					))}
				</ListBox>
			</Select.Popover>
			<FieldError>{errorMessage}</FieldError>
		</Select>
	);
}
