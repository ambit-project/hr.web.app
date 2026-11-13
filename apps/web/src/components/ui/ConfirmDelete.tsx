import { AlertDialog, Button } from "@heroui/react";
import { useTranslation } from "react-i18next";

/** "Usuń" in a table row, confirmed in an alert that only "Anuluj" or the action closes. */
export function ConfirmDelete({
	label,
	title,
	body,
	onConfirm,
}: {
	label: string;
	title: string;
	body: string;
	onConfirm: () => void;
}) {
	const { t } = useTranslation();
	return (
		<AlertDialog>
			<Button size="sm" variant="danger-soft">
				{label}
			</Button>
			<AlertDialog.Backdrop isDismissable={false} isKeyboardDismissDisabled>
				<AlertDialog.Container>
					<AlertDialog.Dialog className="sm:max-w-[420px]">
						<AlertDialog.Header>
							<AlertDialog.Icon status="danger" />
							<AlertDialog.Heading>{title}</AlertDialog.Heading>
						</AlertDialog.Header>
						<AlertDialog.Body>
							<p>{body}</p>
						</AlertDialog.Body>
						<AlertDialog.Footer>
							<Button slot="close" variant="tertiary">
								{t("common.cancel")}
							</Button>
							<Button slot="close" variant="danger" onPress={onConfirm}>
								{label}
							</Button>
						</AlertDialog.Footer>
					</AlertDialog.Dialog>
				</AlertDialog.Container>
			</AlertDialog.Backdrop>
		</AlertDialog>
	);
}
