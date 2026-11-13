import { Checkbox } from "@heroui/react";
import { useFieldContext } from "./context";

export function CheckboxInput({ label }: { label: string }) {
	const field = useFieldContext<boolean>();
	return (
		<Checkbox isSelected={field.state.value} onChange={field.handleChange} name={field.name}>
			<Checkbox.Content>
				<Checkbox.Control>
					<Checkbox.Indicator />
				</Checkbox.Control>
				{label}
			</Checkbox.Content>
		</Checkbox>
	);
}
