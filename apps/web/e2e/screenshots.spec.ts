import { expect, type Page, test } from "@playwright/test";

// Zrzuty ekranu do README (ciemny motyw). Uruchom: yarn e2e:screenshots (stos z docker compose musi działać).
const OUT = "../../docs/screenshots";
// Spółka „Demo” z danych deweloperskich; listy płac zakłada scripts/dev-payrolls.sh.
const DEMO = `companyId=${process.env.E2E_COMPANY_ID ?? "01a1d0db-7fa0-76e5-972d-100e3bd96785"}&year=2026`;

async function shot(page: Page, name: string) {
	await page.waitForLoadState("networkidle");
	await page.waitForTimeout(300);
	await page.screenshot({ path: `${OUT}/${name}.png` });
}

async function openFirst(page: Page) {
	await page.locator("a.open-link").first().click();
	await page.waitForLoadState("networkidle");
}

test.beforeEach(async ({ page }) => {
	await page.addInitScript(() => {
		localStorage.setItem("hr-theme", "dark");
	});
});

test("zrzuty ekranu — ciemny motyw", async ({ page }) => {
	await page.goto("/login");
	await shot(page, "01-login");

	await page.getByRole("button", { name: "Kadry" }).click();
	await page.waitForURL((url) => !url.pathname.startsWith("/login"));

	await page.goto("/employees");
	await expect(page.locator("table")).toBeVisible();
	await shot(page, "02-employees");

	await openFirst(page);
	await shot(page, "03-employee-details");

	await page.goto("/employees");
	await page.locator("button[aria-label]").filter({ has: page.locator("svg.lucide-plus") }).first().click();
	await expect(page.getByRole("dialog")).toBeVisible();
	await shot(page, "04-employee-wizard");

	const pages: [string, string][] = [
		["/companies", "05-companies"],
		["/pay-components", "06-pay-components"],
		[`/calendar?${DEMO}`, "07-calendar"],
		[`/ppk?${DEMO}`, "08-ppk"],
		["/legal-parameters", "09-legal-parameters"],
	];
	for (const [path, name] of pages) {
		await page.goto(path);
		await shot(page, name);
	}

	await page.goto(`/payrolls?${DEMO}`);
	await shot(page, "10-payrolls");
	await openFirst(page);
	await shot(page, "11-payroll");
	await openFirst(page);
	await shot(page, "12-payroll-entry");
	await page.goto(page.url().replace("/payroll-entries/", "/payslips/"));
	await shot(page, "13-payslip");

	await page.goto(`/pit11?${DEMO}`);
	await shot(page, "14-pit11");
	await openFirst(page);
	await shot(page, "15-pit11-form");
	await page.goto(`/tax-summary?${DEMO}`);
	await shot(page, "16-tax-summary");
});
