import { Button, Tooltip } from "@heroui/react";
import { type LucideIcon, Pencil, Plus } from "lucide-react";

type IconButtonProps = {
	label: string;
	onPress: () => void;
	variant?: "primary" | "secondary" | "ghost";
	size?: "sm" | "md";
	isDisabled?: boolean;
};

/**
 * Action as an icon with a tooltip (owner's decision: no "Dodaj"/"Edytuj" captions). The label names the action
 * ("Nowa spółka", "Edytuj kartotekę"), so it is both the tooltip and the accessible name.
 */
export function IconButton({
	icon: Icon,
	label,
	onPress,
	variant = "secondary",
	size = "md",
	isDisabled,
}: IconButtonProps & { icon: LucideIcon }) {
	return (
		<Tooltip delay={200}>
			<Button
				isIconOnly
				aria-label={label}
				variant={variant}
				size={size}
				isDisabled={isDisabled}
				onPress={onPress}
			>
				<Icon className="size-4" />
			</Button>
			<Tooltip.Content>{label}</Tooltip.Content>
		</Tooltip>
	);
}

export function AddButton(props: IconButtonProps) {
	return <IconButton icon={Plus} {...props} variant={props.variant ?? "primary"} />;
}

export function EditButton(props: IconButtonProps) {
	return <IconButton icon={Pencil} {...props} />;
}
