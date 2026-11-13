import { Alert } from "@heroui/react";
import type { ReactNode } from "react";

/** One-line message above a table or in a dialog (warning, missing data). */
export function Notice({
	status = "warning",
	children,
}: {
	status?: "default" | "accent" | "success" | "warning" | "danger";
	children: ReactNode;
}) {
	return (
		<Alert status={status} className="max-w-3xl">
			<Alert.Indicator />
			<Alert.Content>
				<Alert.Description>{children}</Alert.Description>
			</Alert.Content>
		</Alert>
	);
}
