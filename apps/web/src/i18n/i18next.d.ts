import "i18next";
import type { pl } from "./pl";

declare module "i18next" {
	interface CustomTypeOptions {
		resources: { translation: typeof pl };
	}
}
