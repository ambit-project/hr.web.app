import { createFileRoute, redirect } from "@tanstack/react-router";
import { useTranslation } from "react-i18next";
import { homePath } from "#/features/auth/session";

export const Route = createFileRoute("/")({
	beforeLoad: ({ context }) => {
		const home = context.me ? homePath(context.me) : null;
		if (home) throw redirect({ to: home });
	},
	component: NoAccess,
});

function NoAccess() {
	const { t } = useTranslation();
	return <p className="text-muted">{t("auth.noAccess")}</p>;
}
