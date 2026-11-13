import { defineConfig } from "@playwright/test";

// Zrzuty ekranu do README: działający stos (infrastructure/docker-compose.yml), ciemny motyw, dane deweloperskie.
export default defineConfig({
	testDir: "./e2e",
	timeout: 60_000,
	workers: 1,
	reporter: "list",
	use: {
		baseURL: process.env.E2E_BASE_URL ?? "http://127.0.0.1:6301",
		viewport: { width: 1440, height: 900 },
		colorScheme: "dark",
		locale: "pl-PL",
	},
});
