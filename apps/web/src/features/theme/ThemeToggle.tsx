import { Button, Tooltip } from "@heroui/react";
import { Moon, Sun } from "lucide-react";
import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { applyTheme, currentTheme, nextTheme, type Theme } from "./theme";

/** Icon button that switches light ↔ dark; the choice lives in this browser (localStorage). */
export function ThemeToggle() {
	const { t } = useTranslation();
	// The server cannot know the theme the init script picked, so the icon is known only after mounting.
	const [theme, setTheme] = useState<Theme | null>(null);

	useEffect(() => {
		setTheme(currentTheme());
	}, []);

	const label = theme === "dark" ? t("theme.toLight") : t("theme.toDark");
	return (
		<Tooltip delay={300}>
			<Button
				isIconOnly
				size="sm"
				variant="ghost"
				aria-label={label}
				isDisabled={theme === null}
				onPress={() => {
					if (!theme) return;
					const next = nextTheme(theme);
					applyTheme(next);
					setTheme(next);
				}}
			>
				{theme === "dark" ? <Sun className="size-4" /> : <Moon className="size-4" />}
			</Button>
			<Tooltip.Content>{label}</Tooltip.Content>
		</Tooltip>
	);
}
