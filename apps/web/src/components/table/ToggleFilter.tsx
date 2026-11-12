import { Switch } from "@heroui/react";

/** A yes/no list filter kept in the URL (e.g. "show inactive"). */
export function ToggleFilter({
	label,
	value,
	onChange,
}: {
	label: string;
	value: boolean;
	onChange: (value: boolean) => void;
}) {
	return (
		<Switch isSelected={value} onChange={onChange}>
			<Switch.Content>
				<Switch.Control>
					<Switch.Thumb />
				</Switch.Control>
				{label}
			</Switch.Content>
		</Switch>
	);
}
