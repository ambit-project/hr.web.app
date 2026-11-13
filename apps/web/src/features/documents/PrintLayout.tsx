import { Spinner } from "@heroui/react";
import { Printer } from "lucide-react";
import type { ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { IconButton } from "#/components/ui/AddButton";
import { Notice } from "#/features/time-absence/Notice";
import { problemMessage } from "#/forms/feedback";

/**
 * Frame of a printable document (010 R2: printed from the browser; the payslip also has a PDF from the API — 011).
 * `actions` sit before the print button. The toolbar is not printed; the menu, the top bar and the dev banner are hidden by the print styles (base.css).
 */
export function PrintPage({
	back,
	actions,
	error,
	isPending,
	children,
}: {
	back: ReactNode;
	actions?: ReactNode;
	error: unknown;
	isPending: boolean;
	children: ReactNode;
}) {
	const { t } = useTranslation();
	return (
		<section className="flex flex-col gap-4">
			<div className="no-print flex flex-wrap items-center justify-between gap-3">
				<div className="text-sm text-muted">{back}</div>
				<div className="flex gap-2">
					{actions}
					<IconButton
						icon={Printer}
						label={t("documents.print")}
						variant="primary"
						isDisabled={isPending || Boolean(error)}
						onPress={() => window.print()}
					/>
				</div>
			</div>
			{error ? <Notice status="danger">{problemMessage(t, error)}</Notice> : null}
			{isPending && !error ? <Spinner aria-label={t("common.loading")} /> : null}
			{!isPending && !error ? <article className="print-doc">{children}</article> : null}
		</section>
	);
}

/** Labelled value in a document header. */
export function DocField({ label, children }: { label: string; children: ReactNode }) {
	return (
		<div className="print-doc__field">
			<div className="print-doc__label">{label}</div>
			<div className="print-doc__value">{children}</div>
		</div>
	);
}
