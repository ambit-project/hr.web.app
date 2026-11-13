import { toast } from "@heroui/react";
import type { useTranslation } from "react-i18next";
import type { ValidationCode } from "#/forms/validation";
import type { pl } from "#/i18n/pl";
import { ProblemError } from "#/lib/problem";

type TranslateFn = ReturnType<typeof useTranslation>["t"];
type Reason = keyof typeof pl.reasons;

/** Message for a 409 with a reason; an unknown reason reads as a plain conflict. */
function reasonMessage(t: TranslateFn, reason: string): string {
	return t(`reasons.${reason as Reason}`, { defaultValue: t("common.conflict") });
}

/**
 * A failed save: a 409 with a reason (componentInUse, signed, consentInUse…) is a rule and keeps the dialog open; a
 * bare 409 means the row changed meanwhile, so the dialog closes over the refreshed list; field codes land on the
 * fields, and codes for fields the form does not have are shown as a toast.
 */
export function onSaveError(
	t: TranslateFn,
	error: unknown,
	applyErrors: (errors: Record<string, string[]> | undefined) => boolean,
	onDone: () => void,
) {
	if (!(error instanceof ProblemError)) {
		toast.danger(t("common.serverError"));
		return;
	}
	const { problem } = error;
	if (problem.status === 409) {
		if (problem.reason) {
			toast.danger(reasonMessage(t, problem.reason));
		} else {
			toast.warning(t("common.conflict"));
			onDone();
		}
		return;
	}
	if (applyErrors(problem.errors)) return;
	const code = Object.values(problem.errors ?? {})[0]?.[0];
	toast.danger(
		code
			? t(`validation.${code as ValidationCode}`, { defaultValue: t("validation.unknown") })
			: t("common.serverError"),
	);
}

/** A failed action without a form (delete): the reason of a 409 when there is one. */
export function onActionError(t: TranslateFn, error: unknown) {
	if (error instanceof ProblemError && error.problem.status === 409) {
		if (error.problem.reason) {
			toast.danger(reasonMessage(t, error.problem.reason));
		} else {
			toast.warning(t("common.conflict"));
		}
	} else {
		toast.danger(t("common.serverError"));
	}
}
