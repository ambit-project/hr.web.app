import { toast } from "@heroui/react";
import { FileDown } from "lucide-react";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import { IconButton } from "#/components/ui/AddButton";
import { onActionError } from "#/forms/feedback";
import { saveBase64File } from "#/lib/download";
import { fetchPayslipPdf } from "./documentsQueries";

/** File name of a payslip PDF: list number and payment date (the API sends the same data; names stay readable). */
export function payslipPdfName(payrollNumber: string, paymentDate: string): string {
	return `pasek-${payrollNumber.replaceAll("/", "-")}-${paymentDate}.pdf`;
}

/** Downloads the payslip of a closed list as PDF (011 R6–R7). */
export function PayslipPdfButton({
	entryId,
	payrollNumber,
	paymentDate,
	variant,
	size,
}: {
	entryId: string;
	payrollNumber: string;
	paymentDate: string;
	variant?: "primary" | "secondary" | "ghost";
	size?: "sm" | "md";
}) {
	const { t } = useTranslation();
	const [pending, setPending] = useState(false);
	const onPress = async () => {
		setPending(true);
		try {
			const name = payslipPdfName(payrollNumber, paymentDate);
			saveBase64File(name, await fetchPayslipPdf(entryId), "application/pdf");
			toast.success(t("documents.downloaded", { file: name }));
		} catch (failure) {
			onActionError(t, failure);
		} finally {
			setPending(false);
		}
	};
	return (
		<IconButton
			icon={FileDown}
			label={t("documents.downloadPdf")}
			variant={variant}
			size={size}
			isDisabled={pending}
			onPress={() => void onPress()}
		/>
	);
}
