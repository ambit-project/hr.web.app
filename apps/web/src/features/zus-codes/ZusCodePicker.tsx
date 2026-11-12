import { ComboBox, EmptyState, FieldError, Input, Label, ListBox } from "@heroui/react";
import { useQuery } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import type { ZusCodeKind } from "./types";
import { zusCodesQuery } from "./zusCodesQueries";

type ZusCodePickerProps = {
	label: string;
	kind: ZusCodeKind;
	/** Selected code (e.g. "0110"); "" = none. */
	value: string;
	onChange: (code: string) => void;
	onBlur?: () => void;
	isRequired?: boolean;
	errorMessage?: string | null;
};

/** Picks a ZUS code by its code; searches the dictionary by code and name as the user types. */
export function ZusCodePicker({
	label,
	kind,
	value,
	onChange,
	onBlur,
	isRequired,
	errorMessage,
}: ZusCodePickerProps) {
	const { t } = useTranslation();
	const [input, setInput] = useState(value);
	const [term, setTerm] = useState("");

	// A value set from outside (e.g. defaults after changing the contract kind) replaces the text in the box.
	useEffect(() => setInput(value), [value]);

	// The chosen code is not a search term; typing anything else searches after a pause.
	useEffect(() => {
		const next = input === value ? "" : input;
		const handle = setTimeout(() => setTerm(next), 250);
		return () => clearTimeout(handle);
	}, [input, value]);

	const { data } = useQuery(zusCodesQuery({ kind, search: term || undefined }));
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
				if (key === null) return;
				setInput(String(key));
				onChange(String(key));
			}}
			onBlur={onBlur}
			isRequired={isRequired}
			isInvalid={Boolean(errorMessage)}
			validationBehavior="aria"
		>
			<Label>{label}</Label>
			<ComboBox.InputGroup>
				<Input placeholder={t("zusCodes.pickerPlaceholder")} />
				<ComboBox.Trigger />
			</ComboBox.InputGroup>
			<ComboBox.Popover>
				<ListBox
					renderEmptyState={() => (
						<EmptyState className="p-3 text-sm text-muted">{t("zusCodes.empty")}</EmptyState>
					)}
				>
					{items.map((item) => (
						<ListBox.Item key={item.id} id={item.code} textValue={item.code}>
							<span className="line-clamp-2">
								<code>{item.code}</code> {item.name}
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
