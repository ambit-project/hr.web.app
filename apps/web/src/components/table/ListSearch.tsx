import { SearchField } from "@heroui/react";
import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";

type ListSearchProps = {
	value: string | undefined;
	placeholder: string;
	/** Called after a pause with the trimmed term (undefined when empty); the page resets paging. */
	onSearch: (term: string | undefined) => void;
};

export function ListSearch({ value, placeholder, onSearch }: ListSearchProps) {
	const { t } = useTranslation();
	const [term, setTerm] = useState(value ?? "");

	useEffect(() => {
		const handle = setTimeout(() => {
			const next = term.trim() === "" ? undefined : term.trim();
			if (next !== value) onSearch(next);
		}, 300);
		return () => clearTimeout(handle);
	}, [term, value, onSearch]);

	return (
		<SearchField value={term} onChange={setTerm} aria-label={t("common.search")}>
			<SearchField.Group>
				<SearchField.SearchIcon />
				<SearchField.Input className="w-full sm:w-80" placeholder={placeholder} />
				<SearchField.ClearButton />
			</SearchField.Group>
		</SearchField>
	);
}
