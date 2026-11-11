import { test, expect } from "@playwright/test";
import { buildSeats } from "../../src/seatPlans.ts";

async function session(page, role) {
  await page.addInitScript(role => {
    sessionStorage.setItem("travela_token", "journey-token");
    sessionStorage.setItem("travela_user", JSON.stringify({ role, fullName: "Test Kullanıcısı", companyName: "Test Firma" }));
  }, role);
}

for (const width of [390, 1440]) test(`Yolcu gerçek plandan seçer ve terminal adresini görür: ${width}px`, async ({ page }) => {
  await session(page, "Passenger"); await page.setViewportSize({ width, height: 1000 });
  await page.route("**/api/purchases/options", r => r.fulfill({ json: { provider: "LocalTest", requiresBuyer: false } }));
  await page.route("**/api/campaigns/quote", r => r.fulfill({ json: { amount: 700, originalAmount: 700, discountAmount: 0 } }));
  await page.route("**/api/profile", r => r.fulfill({ json: { fullName: "Test Yolcusu", email: "test@example.com", phoneNumber: "+905321234567" } }));
  const trip = { id: 12, companyName: "Test Firma", departureCity: "İstanbul", arrivalCity: "Ankara", departureTime: "2099-01-01T07:00:00Z", arrivalTime: "2099-01-01T13:00:00Z", durationMinutes: 360, price: 700, availableSeats: 6, seatCapacity: 9, status: "Scheduled", canReserve: true, seatPlanAvailable: true, seatLayout: "2+1", seats: buildSeats("2+1", 9, [3, 8]), occupiedSeats: [1], departureTerminalName: "Esenler Otogarı", departureTerminalAddress: "Altıntepsi, İstanbul", arrivalTerminalName: "AŞTİ", arrivalTerminalAddress: "Emek, Ankara", boardingInstructions: "Peron 12'de hazır olun." };
  await page.route("**/api/trips/public/12", r => r.fulfill({ json: trip }));
  let sent;
  await page.route("**/api/purchases", async r => { sent = r.request().postDataJSON(); await r.fulfill({ status: 409, json: { message: "Test sırasında koltuk tutuldu." } }); });
  await page.goto("/trips/12");
  const plan = page.getByRole("group", { name: "Numaralı koltuk planı" });
  await expect(plan.getByRole("button", { name: /^Koltuk 1,/ })).toBeDisabled();
  await expect(plan.getByRole("button", { name: /^Koltuk 3,/ })).toBeDisabled();
  await expect(plan.getByRole("button", { name: /^Koltuk 8,/ })).toBeDisabled();
  await plan.getByRole("button", { name: /^Koltuk 6,/ }).click();
  await expect(plan.getByRole("button", { name: /^Koltuk 6,/ })).toHaveAttribute("aria-pressed", "true");
  const locations = page.getByRole("region", { name: "Terminal ve biniş bilgileri" });
  await expect(locations).toContainText("Esenler Otogarı"); await expect(locations).toContainText("Peron 12");
  await expect(locations.getByRole("link", { name: "Kalkış noktasını haritada aç" })).toHaveAttribute("href", `https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(trip.departureTerminalAddress)}`);
  const size = await page.evaluate(() => ({ width: document.documentElement.scrollWidth, viewport: document.documentElement.clientWidth })); expect(size.width).toBeLessThanOrEqual(size.viewport + 1);
  await page.screenshot({ path: `../tmp/ticket-qa/journey-${width}.png`, fullPage: true });
  await page.getByRole("button", { name: "Hesap bilgilerimi kullan · Koltuk 6" }).click();
  await page.locator('input[name="gender-6"][value="Female"]').check();
  await page.getByRole("checkbox", { name: /Yolcu bilgilerini kontrol ettim/ }).check();
  await page.getByRole("checkbox", { name: /Mesafeli satış sözleşmesini ve ön bilgilendirme/ }).check();
  await page.getByRole("button", { name: "Ödemeye geç", exact: true }).click();
  await expect(page.getByRole("alert")).toContainText("Test sırasında koltuk tutuldu"); expect(sent.passengers[0].seatNumber).toBe(6); expect(sent.tripId).toBe(12);
});

test("Firma otobüs formunda 2+1 planını ve satışa kapalı koltukları kaydeder", async ({ page }) => {
  await session(page, "Company");
  await page.route("**/api/company/profile", r => r.fulfill({ json: { id: 1, name: "Test Firma" } }));
  await page.route("**/api/buses/summary", r => r.fulfill({ json: { totalBuses: 0, activeBuses: 0, inactiveBuses: 0, companiesWithBuses: 0 } }));
  await page.route("**/api/buses?*", r => r.fulfill({ json: { items: [], totalCount: 0 } }));
  let sent;
  await page.route("**/api/buses", async r => { sent = r.request().postDataJSON(); await r.fulfill({ status: 201, json: { message: "Otobüs kaydedildi." } }); });
  await page.goto("/company/buses"); await page.getByRole("button", { name: /Yeni Otobüs Ekle/ }).click();
  await page.getByLabel("Plaka", { exact: false }).first().fill("34 TEST 123");
  await page.getByLabel("Fiziksel Koltuk Kapasitesi", { exact: false }).fill("9");
  await page.getByLabel("Koltuk düzeni", { exact: true }).selectOption("2+1");
  await page.getByRole("button", { name: /^Koltuk 3,/ }).click();
  await page.getByRole("checkbox", { name: "Koltuk planını gerçek otobüsle karşılaştırdım ve doğruluyorum." }).check();
  await page.getByRole("button", { name: "Kaydet", exact: true }).click();
  await expect(page.getByText("Otobüs kaydedildi.", { exact: true })).toBeVisible();
  expect(sent.seatLayout).toBe("2+1"); expect(sent.blockedSeats).toEqual([3]); expect(sent.seatCapacity).toBe(9);
});

test("Yönetici terminal ekler ve firma menüsünden bağımsız katalogda düzenler", async ({ page }) => {
  await session(page, "Admin"); let sent; let terminals = [];
  await page.route("**/api/terminals?includeInactive=true", r => r.fulfill({ json: terminals }));
  await page.route("**/api/terminals", async r => { sent = r.request().postDataJSON(); terminals = [{ ...sent, id: 1 }]; await r.fulfill({ status: 201, json: { message: "Terminal oluşturuldu." } }); });
  await page.goto("/admin/terminals"); await page.getByLabel("Terminal adı", { exact: true }).fill("Esenler Otogarı");
  await page.getByLabel("Şehir", { exact: true }).fill("İstanbul"); await page.getByLabel("Açık adres", { exact: true }).fill("Altıntepsi Mahallesi");
  await page.getByRole("button", { name: "Terminali kaydet" }).click();
  await expect(page.getByRole("table")).toContainText("Esenler Otogarı"); expect(sent.isActive).toBe(true);
  await page.getByRole("button", { name: "Düzenle" }).click(); await expect(page.getByLabel("Açık adres", { exact: true })).toHaveValue("Altıntepsi Mahallesi");
});
