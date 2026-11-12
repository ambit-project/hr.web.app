import { Switch } from "@heroui/react";
import { useFieldContext } from "./context";

export function SwitchInput({ label }: { label: string }) {
	const field = useFieldContext<boolean>();
	return (
		<Switch isSelected={field.state.value} onChange={field.handleChange} name={field.name}>
			<Switch.Content>
				<Switch.Control>
					<Switch.Thumb />
				</Switch.Control>
				{label}
			</Switch.Content>
		</Switch>
	);
}
