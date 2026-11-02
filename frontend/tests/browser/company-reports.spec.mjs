import { test, expect } from "@playwright/test";
import { readFileSync, mkdirSync } from "node:fs";
import path from "node:path";

// SQL/HTTP suite captures these synthetic ledgers. Authorization and arithmetic
// are exercised against SQL there; these tests cover the real React UI/CSS.
const directory = path.resolve("../tmp/report-qa");
const local = JSON.parse(readFileSync(path.join(directory, "test.json"), "utf8"));
const real = JSON.parse(readFileSync(path.join(directory, "real.json"), "utf8"));
mkdirSync(path.join(directory, "screenshots"), { recursive: true });
const filter = "period=custom&from=2026-11-01&to=2026-11-01&environment=test&pageSize=1";

async function setup(page, width, handler) {
 await page.setViewportSize({ width, height: 900 });
 await page.addInitScript(() => {
  sessionStorage.setItem("travela_token", "isolated-report-layout-token");
  sessionStorage.setItem("travela_user", JSON.stringify({ role: "Company", name: "Test Firma", companyName: "Rapor Firma" }));
 });
 await page.route("**/api/company/profile", r => r.fulfill({ json: { name: "Rapor Firma" } }));
 await page.route("**/api/company/reports/options", r => r.fulfill({ json: [{ id: 1, departureCity: "İstanbul", arrivalCity: "İzmir" }] }));
 await page.route(/\/api\/company\/reports\?/, handler ?? (r => r.fulfill({ json: new URL(r.request().url()).searchParams.get("environment") === "test" ? local : real })));
}
for (const width of [360, 390, 1440]) for (const overview of [true, false]) {
 test(`${overview ? "Genel Bakış" : "Raporlar"}: ${width}px, kapsam ve yatay taşma`, async ({ page }) => {
  await setup(page, width); await page.goto(`/company${overview ? "" : "/reports"}?${filter}`);
  await expect(page.getByRole("heading", { name: "Satılan bilet", exact: true })).toBeVisible();
  await expect(page.locator(".test-environment-notice")).toHaveCount(1);
  await expect(page.locator(".report-card-grid")).not.toContainText("YEREL TEST");
  await expect(page.locator(".report-card-grid .company-stat-card").first()).toContainText("5");
  await page.getByText("Rapor hakkında", { exact: true }).focus();
  await page.keyboard.press("Enter");
  await expect(page.locator(".report-scope .ui-details-content")).toBeVisible();
  await expect(page.locator(".report-scope")).toContainText("durum tarihi bilinmiyor");
  await page.keyboard.press("Enter");
  await expect(page.locator(".report-scope .ui-details-content")).toBeHidden();
  if (!overview) {
   await expect(page.getByRole("img")).toHaveCount(2);
   await page.getByText("Grafik verileri", { exact: true }).first().click();
   await expect(page.locator(".report-chart table").first()).toBeVisible();
   const scroll = await page.locator(".report-trip-wrap").evaluate(el => ({ client: el.clientWidth, scroll: el.scrollWidth }));
   expect(scroll.scroll).toBeGreaterThan(scroll.client);
  }
  const overflow = await page.evaluate(() => ({ viewport: document.documentElement.clientWidth, width: document.documentElement.scrollWidth,
   outside: [...document.querySelectorAll(".report-filters input, .report-filters select, .company-reporting button, .company-stat-card")].filter(el => el.getClientRects().length && (el.getBoundingClientRect().right > innerWidth + 1 || el.getBoundingClientRect().left < -1)).map(el => el.textContent) }));
  expect(overflow.width).toBeLessThanOrEqual(overflow.viewport + 1); expect(overflow.outside).toEqual([]);
  await page.screenshot({ path: path.join(directory, "screenshots", `${overview ? "overview" : "reports"}-${width}.png`), fullPage: true });
 });
}
test("Filtre URL'de ve yenilemede korunur; CSV seçili kapsamla indirilir", async ({ page }) => {
 await setup(page, 390); await page.goto(`/company/reports?${filter}`);
 await expect(page.getByRole("heading", { name: "Satılan bilet", exact: true })).toBeVisible();
 await page.getByRole("combobox", { name: "Rapor kapsamı", exact: true }).selectOption("departure");
 await page.getByLabel("Sefer numarası", { exact: true }).fill("12");
 await page.getByRole("button", { name: "Filtreleri uygula" }).click();
 await expect(page).toHaveURL(/basis=departure/); await expect(page).toHaveURL(/tripId=12/);
 await page.reload(); await expect(page.getByLabel("Sefer numarası", { exact: true })).toHaveValue("12");
 await expect(page.getByRole("combobox", { name: "İşlem ortamı", exact: true })).toHaveValue("test");
 let csvQuery;
 await page.route("**/api/company/reports/csv?*", async route => { csvQuery = new URL(route.request().url()).searchParams; await route.fulfill({ contentType: "text/csv; charset=utf-8", body: readFileSync(path.join(directory, "test.csv")) }); });
 const download = page.waitForEvent("download"); await page.getByRole("button", { name: "CSV indir" }).click();
 expect((await download).suggestedFilename()).toContain("YEREL-TEST"); expect(csvQuery.get("environment")).toBe("test"); expect(csvQuery.get("tripId")).toBe("12"); expect(csvQuery.get("basis")).toBe("departure");
});
test("Eski istek yeni ortam sonucunu ezmez ve gerçek boş rapor test verisine geçmez", async ({ page }) => {
 let delayed = false;
 const empty = { ...real, metrics: Object.fromEntries(Object.keys(real.metrics).map(k => [k, 0])), trips: [], routes: [], totalTrips: 0, recent: [], failedRefunds: [] };
 await setup(page, 390, async route => {
  const isTest = new URL(route.request().url()).searchParams.get("environment") === "test";
  if (isTest) { delayed = true; await new Promise(resolve => setTimeout(resolve, 800)); }
  await route.fulfill({ json: isTest ? local : empty }).catch(() => {});
 });
 await page.goto(`/company/reports?${filter}`); await expect.poll(() => delayed).toBe(true);
 await page.getByRole("combobox", { name: "İşlem ortamı", exact: true }).selectOption("real"); await page.getByRole("button", { name: "Filtreleri uygula" }).click();
 await expect(page.locator(".report-scope")).toContainText("Bu dönemde işlem bulunmuyor");
 await page.waitForTimeout(1000); await expect(page.locator(".report-card-grid")).not.toContainText("YEREL TEST");
 await expect(page.locator(".report-card-grid .company-stat-card").first().locator(".report-card-value")).toHaveText("0");
});
test("Sunucu hatası ve yeniden deneme Türkçe; geçersiz özel tarih engellenir", async ({ page }) => {
 let fail = true;
 await setup(page, 360, route => route.fulfill(fail ? { status: 500, json: { message: "Rapor verileri alınamadı." } } : { json: local }));
 await page.goto(`/company/reports?${filter}`); await expect(page.getByRole("alert")).toContainText("Rapor verileri alınamadı");
 fail = false; await page.getByRole("button", { name: "Tekrar dene" }).click(); await expect(page.getByRole("heading", { name: "Satılan bilet", exact: true })).toBeVisible();
 await page.getByLabel("Başlangıç", { exact: true }).fill("2025-01-01");
 await page.getByRole("button", { name: "Filtreleri uygula" }).click(); await expect(page.getByRole("alert")).toContainText("en fazla 366 gün");
});

