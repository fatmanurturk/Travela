import { test, expect } from "@playwright/test";
import { readFileSync, mkdirSync } from "node:fs";
import path from "node:path";

const directory = path.resolve("../tmp/ticket-qa");
const active = JSON.parse(readFileSync(path.join(directory, "active.json"), "utf8"));
const cancelled = JSON.parse(readFileSync(path.join(directory, "cancelled.json"), "utf8"));
const legacy = { ...active, reservationNumber: "1234567890ABCDEF1234567890ABCDEF", price: active.amount, status: "Active", kind: "Legacy", isTest: false, pnr: null, createdAt: active.paidAt, tripStatus: "Scheduled", canCancel: true };
const pending = { ...active, pnr: null, kind: "Hold", paymentStatus: "Pending", legalAccepted: true, ticketStatus: "Active", refundStatus: "None", canCancel: false, holdExpiresAt: new Date(Date.now() + 600000).toISOString(), checkoutUrl: `/test-payment/${active.id}` };
const shots = path.resolve("../tmp/ui-simplification"); mkdirSync(shots, { recursive: true });
async function prepare(page, width, company = false) {
 await page.setViewportSize({ width, height: 900 });
 await page.addInitScript(({ company }) => {
  sessionStorage.setItem("travela_token", "isolated-layout-token");
  sessionStorage.setItem("travela_user", JSON.stringify({ role: company ? "Company" : "Passenger", fullName: "Test Yolcu", companyName: "Test Firma" }));
  Object.defineProperty(navigator, "clipboard", { configurable: true, value: { writeText: async value => { window.__copiedIdentifier = value; } } });
 }, { company });
 await page.route("**/api/company/profile", route => route.fulfill({ json: { name: "Test Firma" } }));
 await page.route("**/api/purchases/options", route => route.fulfill({ json: { provider: "LocalTest", requiresBuyer: false } }));
 await page.route("**/api/profile", route => route.fulfill({ json: { fullName: "Test Yolcu", email: "test@example.com", phoneNumber: "+905321234567" } }));
}
async function verifyLayout(page, name, width) {
 const result = await page.evaluate(() => ({ width: document.documentElement.scrollWidth, viewport: innerWidth,
  outside: [...document.querySelectorAll("main button, main input, main select, main .site-button")].filter(e => e.getClientRects().length && !e.closest(".table-wrap") && (e.getBoundingClientRect().right > innerWidth + 1 || e.getBoundingClientRect().left < -1)).map(e => e.textContent) }));
 expect(result.width).toBeLessThanOrEqual(result.viewport + 1); expect(result.outside).toEqual([]);
 await page.screenshot({ path: path.join(shots, `${name}-${width}.png`), fullPage: true });
}
for (const width of [360, 390, 1440]) {
 test(`Biletlerim ${width}px: PNR, eski kayıt, kopyalama ve iptal özeti`, async ({ page }) => {
  await prepare(page, width);
  await page.route("**/api/tickets/mine", r => r.fulfill({ json: [active, { ...cancelled, id: "another-ticket", isTest: false }] }));
  await page.route("**/api/reservations/mine", r => r.fulfill({ json: [legacy] }));
  await page.goto("/tickets");
  await expect(page.locator(".reservation-list").first()).toContainText(active.pnr);
  await expect(page.getByText("Aktif bilet", { exact: true })).toBeVisible();
  await expect(page.getByText("İptal edildi", { exact: true }).first()).toBeVisible();
  await expect(page.getByText("Sunucunun hesapladığı tutar")).toHaveCount(0);
  await expect(page.getByText("Bilet tutarı", { exact: true }).first()).toBeVisible();
  const details = page.getByText("Eski ücretsiz kayıtlar (1)", { exact: true });
  await expect(page.getByRole("button", { name: "İşlem numarasını kopyala" })).toBeHidden();
  await details.focus(); await page.keyboard.press("Enter");
  await page.getByRole("button", { name: "İşlem numarasını kopyala" }).click();
  await expect.poll(() => page.evaluate(() => window.__copiedIdentifier)).toBe(legacy.reservationNumber);
  await details.click();
  await verifyLayout(page, "tickets", width);
  await page.getByRole("button", { name: "Bileti İptal Et", exact: true }).click();
  await expect(page.getByRole("dialog")).toContainText("Tam iade tutarı");
  await expect(page.getByRole("dialog")).toContainText("İptal son zamanı");
  await page.getByRole("button", { name: "Vazgeç", exact: true }).click();
 });
 test(`Yerel test ödemesi ${width}px: ödeme ve iade kontrolleri`, async ({ page }) => {
  await prepare(page, width);
  await page.route("**/api/tickets/orders/*", r => r.fulfill({ json: pending }));
  await page.goto(`/test-payment/${active.id}`);
  await expect(page.getByRole("heading", { name: "Ödeme işlemleri" })).toBeVisible();
  await expect(page.getByRole("combobox", { name: "İade sonucu" })).toBeVisible();
  await expect(page.locator("main")).not.toContainText("adaptör"); await expect(page.locator("main")).not.toContainText("sunucu sorgusu");
  await expect(page.getByRole("button", { name: "Ödemeyi başarılı yap" })).toBeVisible();
  await verifyLayout(page, "checkout", width);
 });
 test(`Firma bilet tablosu ${width}px: PNR ve kapalı eski kayıtlar`, async ({ page }) => {
  await prepare(page, width, true);
  await page.route("**/api/reservations/company?*", r => r.fulfill({ json: { items: [{ ...active, reservationNumber: "ticket-record", price: active.amount, status: "Active" }, legacy], totalCount: 2 } }));
  await page.goto("/company/reservations");
  await expect(page.locator(".company-reservation-table").first()).toContainText(active.pnr);
  await expect(page.locator(".company-reservation-table").first()).toContainText("Ödeme doğrulandı");
  await expect(page.getByRole("button", { name: "İşlem numarasını kopyala" })).toBeHidden();
  const summary = page.getByText("Eski ücretsiz kayıtlar", { exact: true }).first(); await summary.click();
  await expect(page.getByRole("button", { name: "İşlem numarasını kopyala" })).toBeVisible(); await summary.click();
  const size = await page.locator(".table-wrap").first().evaluate(e => ({ width: e.clientWidth, scroll: e.scrollWidth }));
  if (width < 1000) expect(size.scroll).toBeGreaterThan(size.width);
  await verifyLayout(page, "company-tickets", width);
 });
 test(`Sefer detayı ${width}px: koltuk seçimi ve kısa ödeme özeti`, async ({ page }) => {
  await prepare(page, width);
  await page.route("**/api/trips/public/*", r => r.fulfill({ json: { id: active.tripId, companyName: active.companyName, departureCity: active.departureCity, arrivalCity: active.arrivalCity, departureTime: active.departureTime, arrivalTime: active.arrivalTime, durationMinutes: 360, price: active.amount, availableSeats: 19, seatCapacity: 20, occupiedSeats: [1], canReserve: true, seatPlanAvailable: true, departureHasPassed: false, status: "Scheduled" } }));
  await page.goto(`/trips/${active.tripId}`);
  await expect(page.getByRole("button", { name: "Koltuk 1, dolu", exact: true })).toBeDisabled();
  await page.getByRole("button", { name: "Koltuk 2, boş", exact: true }).click();
  await expect(page.locator(".booking-summary")).toContainText("Koltuk 2");
  await expect(page.getByRole("button", { name: "Ödemeye geç", exact: true })).toBeEnabled();
  await expect(page.locator("main")).not.toContainText("dahili tutma");
  await verifyLayout(page, "trip", width);
 });
}
test("Kopyalama izni yoksa tam işlem numarası erişilebilir kalır", async ({ page }) => {
 await prepare(page, 390);
 await page.addInitScript(() => { navigator.clipboard.writeText = async () => { throw new Error("İzin verilmedi"); }; });
 await page.route("**/api/tickets/mine", r => r.fulfill({ json: [] }));
 await page.route("**/api/reservations/mine", r => r.fulfill({ json: [legacy] }));
 await page.goto("/tickets");
 await page.getByText("Eski ücretsiz kayıtlar (1)", { exact: true }).click();
 await page.getByRole("button", { name: "İşlem numarasını kopyala" }).click();
 await expect(page.locator(".short-identifier [role=status]")).toHaveText(legacy.reservationNumber);
});
