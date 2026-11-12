import { useTranslation } from "react-i18next";

export function MyPage() {
	const { t } = useTranslation();
	return (
		<section className="flex flex-col gap-2">
			<h1 className="text-2xl font-semibold">{t("my.title")}</h1>
			<p className="text-muted">{t("my.empty")}</p>
		</section>
	);
}
