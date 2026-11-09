import { chromium, expect } from "@playwright/test";
import { mkdirSync } from "node:fs";

// The verifier supplies a short-lived token only through this child process's environment.
// No API requests are mocked and no application records are created or updated by this script.
const token = process.env.TRAVELA_VERIFY_TOKEN;
if (!token) throw new Error("Canlı doğrulama oturumu gerekli.");
const total = Number(process.env.TRAVELA_VERIFY_TOTAL);
const routeTrips = Number(process.env.TRAVELA_VERIFY_ROUTE9_COUNT);
const browser = await chromium.launch({ channel: "chrome", headless: true });
try {
  const page = await browser.newPage({ baseURL: "http://localhost:5173", viewport: { width: 1440, height: 1000 } });
  await page.addInitScript(token => {
    sessionStorage.setItem("travela_token", token);
    sessionStorage.setItem("travela_user", JSON.stringify({ role: "Company", name: "Firma Kullanıcısı" }));
  }, token);
  await page.goto("/company/routes");
  await expect(page.locator(".route-stats .stat-card h2").last()).toHaveText(String(total));
  const route9 = page.locator(".route-table tbody tr").filter({ has: page.getByRole("cell", { name: "#9", exact: true }) });
  await expect(route9.locator("td").nth(5)).toHaveText(String(routeTrips));
  const detailResponse = page.waitForResponse(response => new URL(response.url()).pathname === "/api/routes/9");
  await route9.getByRole("button", { name: "Detay", exact: true }).click();
  const response = await detailResponse;
  expect(response.status()).toBe(200); expect(response.request().method()).toBe("GET");
  await expect(page.getByRole("heading", { name: "Güzergâh #9 Detayları" })).toBeVisible();
  await expect(page.locator(".route-detail tbody tr")).toHaveCount(Math.min(10, routeTrips));
  await expect(page.locator(".route-detail").getByRole("columnheader", { name: "Varış Tarihi", exact: true })).toBeVisible();
  await expect(page.getByRole("alert")).toHaveCount(0);
  mkdirSync("tmp/route-qa", { recursive: true });
  await page.screenshot({ path: "tmp/route-qa/live-route9.png", fullPage: true });
  console.log(`OK: Canlı tarayıcı ID 9 GET HTTP 200; detay ${routeTrips} sefer, kart ${total} sefer.`);
  await page.reload();
  await expect(page.locator(".route-stats .stat-card h2").last()).toHaveText(String(total));
  console.log("OK: Canlı sayfa yenilemesinde toplam sefer sayısı korunur; NaN oluşmaz.");
  await page.getByRole("link", { name: "Otobüs Yönetimi", exact: true }).click();
  await expect(page.locator(".bus-data-table")).toBeVisible();
  await expect(page.getByRole("alert")).toHaveCount(0);
  console.log("OK: Canlı Otobüs Yönetimi listesi hatasız yüklenir.");
  await page.getByRole("link", { name: "Sefer Yönetimi", exact: true }).click();
  await expect(page.locator(".trip-data-table")).toBeVisible();
  await expect(page.getByRole("alert")).toHaveCount(0);
  console.log("OK: Canlı Sefer Yönetimi listesi hatasız yüklenir.");
} finally { await browser.close(); }
