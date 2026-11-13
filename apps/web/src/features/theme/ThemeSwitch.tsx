import { ToggleButton, ToggleButtonGroup } from "@heroui/react";
import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { applyTheme, readPreference, THEME_PREFERENCES, type ThemePreference } from "./theme";

/** Light / dark / system switch; the choice lives in this browser only (localStorage). */
export function ThemeSwitch() {
	const { t } = useTranslation();
	// The server cannot know the stored choice, so the switch shows it only after mounting.
	const [preference, setPreference] = useState<ThemePreference | null>(null);

	useEffect(() => {
		setPreference(readPreference());
	}, []);

	useEffect(() => {
		if (preference !== "system") return;
		const media = window.matchMedia("(prefers-color-scheme: dark)");
		const follow = () => applyTheme("system");
		media.addEventListener("change", follow);
		return () => media.removeEventListener("change", follow);
	}, [preference]);

	return (
		<ToggleButtonGroup
			aria-label={t("theme.label")}
			size="sm"
			selectionMode="single"
			disallowEmptySelection
			selectedKeys={preference ? [preference] : []}
			onSelectionChange={(keys) => {
				const next = [...keys][0] as ThemePreference | undefined;
				if (!next) return;
				setPreference(next);
				applyTheme(next);
			}}
		>
			{THEME_PREFERENCES.map((option) => (
				<ToggleButton key={option} id={option}>
					{t(`theme.${option}`)}
				</ToggleButton>
			))}
		</ToggleButtonGroup>
	);
}
