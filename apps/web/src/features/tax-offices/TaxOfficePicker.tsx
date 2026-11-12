import { ComboBox, EmptyState, FieldError, Input, Label, ListBox } from "@heroui/react";
import { useQuery } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { taxOfficePickerQuery } from "./taxOfficesQueries";

type TaxOfficePickerProps = {
	label: string;
	/** Selected office id; "" = none. */
	value: string;
	/** Label of the office already saved (e.g. "1438 URZĄD…"), shown before the list loads. */
	initialLabel?: string;
	onChange: (id: string) => void;
	onBlur?: () => void;
	isRequired?: boolean;
	errorMessage?: string | null;
};

export function TaxOfficePicker({
	label,
	value,
	initialLabel,
	onChange,
	onBlur,
	isRequired,
	errorMessage,
}: TaxOfficePickerProps) {
	const { t } = useTranslation();
	const [input, setInput] = useState(initialLabel ?? "");
	const [selectedLabel, setSelectedLabel] = useState(initialLabel ?? "");
	const [term, setTerm] = useState("");

	// The text of the chosen office is not a search term; typing anything else searches after a pause.
	useEffect(() => {
		const next = input === selectedLabel ? "" : input;
		const handle = setTimeout(() => setTerm(next), 250);
		return () => clearTimeout(handle);
	}, [input, selectedLabel]);

	const { data } = useQuery(taxOfficePickerQuery(term));
	const items = data?.items ?? [];

	return (
		<ComboBox
			allowsEmptyCollection
			menuTrigger="focus"
			inputValue={input}
			onInputChange={(text) => {
				setInput(text);
				if (text === "") onChange("");
			}}
			selectedKey={value === "" ? null : value}
			onSelectionChange={(key) => {
				const office = items.find((item) => item.id === key);
				if (!office) return;
				const officeLabel = `${office.code} ${office.name}`;
				setSelectedLabel(officeLabel);
				setInput(officeLabel);
				onChange(office.id);
			}}
			onBlur={onBlur}
			isRequired={isRequired}
			isInvalid={Boolean(errorMessage)}
			validationBehavior="aria"
		>
			<Label>{label}</Label>
			<ComboBox.InputGroup>
				<Input placeholder={t("taxOffices.pickerPlaceholder")} />
				<ComboBox.Trigger />
			</ComboBox.InputGroup>
			<ComboBox.Popover>
				<ListBox
					renderEmptyState={() => (
						<EmptyState className="p-3 text-sm text-muted">{t("taxOffices.empty")}</EmptyState>
					)}
				>
					{items.map((office) => (
						<ListBox.Item
							key={office.id}
							id={office.id}
							textValue={`${office.code} ${office.name}`}
						>
							<span>
								<code>{office.code}</code> {office.name}
							</span>
							<ListBox.ItemIndicator />
						</ListBox.Item>
					))}
				</ListBox>
			</ComboBox.Popover>
			<FieldError>{errorMessage}</FieldError>
		</ComboBox>
	);
}
