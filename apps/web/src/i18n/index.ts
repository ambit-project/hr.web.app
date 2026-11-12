import i18next from "i18next";
import { initReactI18next } from "react-i18next";
import { pl } from "./pl";

export const DEFAULT_LANGUAGE = "pl";

// Resources are bundled and init is synchronous, so the server render and the browser see the same texts.
if (!i18next.isInitialized) {
	void i18next.use(initReactI18next).init({
		lng: DEFAULT_LANGUAGE,
		fallbackLng: DEFAULT_LANGUAGE,
		resources: { pl: { translation: pl } },
		interpolation: { escapeValue: false },
		initAsync: false,
	});
}

export { i18next };
