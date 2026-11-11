import { test, expect } from "@playwright/test";
import { chooseSeat } from "./helpers/seat-selection.mjs";

async function setup(page) {
  await page.addInitScript(() => { sessionStorage.setItem("travela_token", "plan-manager"); sessionStorage.setItem("travela_user", JSON.stringify({ role: "Company", fullName: "Firma Test" })); });
  const bus = { id: 2, companyId: 1, companyName: "Test Firma", plateNumber: "34 TRV 2026", brand: "Test", model: "Otobüs", seatCapacity: 46, seatLayout: "Unspecified", seatPlanJson: null, isActive: true, createdAt: "2026-01-01T00:00:00Z", updatedAt: null };
  await page.route("**/api/company/profile", r => r.fulfill({ json: { id: 1, name: "Test Firma", isActive: true } }));
  await page.route("**/api/buses/summary", r => r.fulfill({ json: { totalBuses: 1, activeBuses: 1, inactiveBuses: 0, companiesWithBuses: 1 } }));
  await page.route("**/api/buses?*", r => r.fulfill({ json: { items: [bus], totalCount: 1, pageNumber: 1, pageSize: 10, totalPages: 1 } }));
  const state = { saved: null, calls: 0, conflict: false };
  await page.route("**/api/buses/2/seat-plan", r => {
    if (r.request().method() === "PUT") {
      state.calls++; const draft = r.request().postDataJSON();
      if (state.conflict) return r.fulfill({ status: 409, json: { message: "Sefer #21: Aktif rezervasyon veya bilet var; fiziksel plan değiştirilemez. Hiçbir plan değiştirilmedi." } });
      state.saved = draft; bus.seatLayout = draft.layout; bus.seatPlanJson = JSON.stringify(draft.seats);
      return r.fulfill({ json: { message: "Doğrulanmış otobüs planı kaydedildi ve seçilen 1 sefere aktarıldı.", appliedTripIds: draft.applyToTripIds } });
    }
    return r.fulfill({ json: { id: 2, plateNumber: bus.plateNumber, capacity: 46, layout: state.saved?.layout ?? "Unspecified", seats: state.saved?.seats ?? [], verified: !!state.saved, version: state.saved ? "v2" : "v1", trips: [
      { id: 21, departureCity: "İstanbul", arrivalCity: "Ankara", departureTime: "2099-11-10T13:00:00Z", seatLayout: state.saved ? state.saved.layout : "Unspecified", activeReservations: 0, canApply: !state.saved, reason: state.saved ? "Seferin fiziksel planı zaten doğrulanmış; mevcut plan korunur." : null },
      { id: 22, departureCity: "İstanbul", arrivalCity: "Ankara", departureTime: "2099-11-11T13:00:00Z", seatLayout: "2+1", activeReservations: 2, canApply: false, reason: "Aktif rezervasyon veya bilet var; fiziksel plan değiştirilemez." },
    ] } });
  });
  await page.route("**/api/trips/public/21", r => r.fulfill({ json: { id: 21, companyName: "Test Firma", departureCity: "İstanbul", arrivalCity: "Ankara", departureTime: "2099-11-10T13:00:00Z", arrivalTime: "2099-11-10T19:00:00Z", durationMinutes: 360, price: 400, availableSeats: 46, seatCapacity: 46, seatLayout: state.saved?.layout ?? "Unspecified", seats: state.saved?.seats ?? [], occupiedSeats: [], seatOccupancies: [], canReserve: !!state.saved, seatPlanAvailable: !!state.saved, status: "Scheduled" } }));
  await page.route("**/api/purchases/options", r => r.fulfill({ json: { provider: "LocalTest", requiresBuyer: false } }));
  await page.goto("/company/buses"); await page.getByRole("button", { name: "Koltuk Planı Yönetimi", exact: true }).click();
  return state;
}

for (const [layout, width] of [["2+2", 1440], ["2+1", 390]]) test(`${layout} 46 koltuk planı firma onayıyla seçilen sefere aktarılır (${width}px)`, async ({ page }) => {
  await page.setViewportSize({ width, height: 1000 }); const state = await setup(page);
  const panel = page.getByRole("region", { name: "Koltuk Planı Yönetimi", exact: true });
  await expect(panel.getByLabel("Koltuk düzeni", { exact: true })).toHaveValue("");
  const save = panel.getByRole("button", { name: "Doğrulanmış planı kaydet", exact: true });
  await expect(save).toBeDisabled(); await expect(panel.locator(".physical-seat")).toHaveCount(0);
  await panel.getByLabel("Koltuk düzeni", { exact: true }).selectOption(layout);
  await panel.getByRole("button", { name: "Kapasiteye göre taslak oluştur", exact: true }).click();
  await expect(panel.locator(".physical-seat")).toHaveCount(46); await expect(save).toBeDisabled();
  await panel.getByRole("button", { name: /^Koltuk 3,/ }).click();
  await expect(panel.getByRole("status")).toContainText(layout === "2+1" ? "Yok · Tekli koltuk" : "4");
  await expect(panel.getByRole("checkbox", { name: /Sefer #22/ })).toBeDisabled();
  await panel.getByRole("checkbox", { name: /Planın gerçek otobüsün/ }).check();
  const dimensions = await page.evaluate(() => ({ viewport: innerWidth, width: document.documentElement.scrollWidth })); expect(dimensions.width).toBeLessThanOrEqual(dimensions.viewport + 1);
  await panel.getByRole("checkbox", { name: /Sefer #21/ }).check();
  await expect(panel.getByRole("checkbox", { name: /Planın gerçek otobüsün/ })).not.toBeChecked();
  await expect(save).toBeDisabled(); expect(state.calls).toBe(0);
  await panel.getByRole("checkbox", { name: /Planın gerçek otobüsün/ }).check();
  await page.screenshot({ path: `../tmp/ticket-qa/plan-management-${layout}-${width}.png`, fullPage: true });
  await save.click(); await expect(panel.getByText(/Doğrulanmış otobüs planı kaydedildi ve seçilen 1 sefere aktarıldı/)).toBeVisible();
  expect(state.saved.seats).toHaveLength(46); expect(state.saved.layout).toBe(layout); expect(state.saved.expectedPlanVersion).toBe("v1"); expect(state.saved.applyToTripIds).toEqual([21]); expect(state.saved.confirmed).toBe(true);
  await panel.getByRole("link", { name: /Sefer #21 koltuklarını görüntüle/ }).click();
  await expect(page.getByRole("group", { name: "Numaralı koltuk planı" }).getByRole("button")).toHaveCount(46);
  await chooseSeat(page, 1);
});

test("Firma fiziksel konumu düzenler; çakışma ve düzenleme onayı geçersiz kılar", async ({ page }) => {
  await setup(page); const panel = page.getByRole("region", { name: "Koltuk Planı Yönetimi", exact: true });
  await panel.getByLabel("Koltuk düzeni", { exact: true }).selectOption("2+2"); await panel.getByRole("button", { name: "Kapasiteye göre taslak oluştur", exact: true }).click();
  await panel.getByRole("checkbox", { name: /Planın gerçek otobüsün/ }).check();
  await panel.getByLabel("Önden satır", { exact: true }).fill("30");
  await expect(panel.getByRole("status")).toContainText("Tekli koltuk"); await expect(panel.getByRole("checkbox", { name: /Planın gerçek otobüsün/ })).not.toBeChecked();
  await panel.getByLabel("Koltuk numarası", { exact: true }).fill("2"); await expect(panel.getByRole("alert")).toContainText("numaraları benzersiz");
  await expect(panel.getByRole("button", { name: "Doğrulanmış planı kaydet" })).toBeDisabled();
  await panel.getByLabel("Koltuk numarası", { exact: true }).fill("1"); await expect(panel.getByRole("alert")).toHaveCount(0);
});

test("Kaydetme sırasında yeni rezervasyon oluşursa API hatası gösterilir ve sahte başarı oluşmaz", async ({ page }) => {
  const state = await setup(page); state.conflict = true; const panel = page.getByRole("region", { name: "Koltuk Planı Yönetimi", exact: true });
  await panel.getByLabel("Koltuk düzeni", { exact: true }).selectOption("2+2"); await panel.getByRole("button", { name: "Kapasiteye göre taslak oluştur", exact: true }).click();
  await panel.getByRole("checkbox", { name: /Sefer #21/ }).check(); await panel.getByRole("checkbox", { name: /Planın gerçek otobüsün/ }).check(); await panel.getByRole("button", { name: "Doğrulanmış planı kaydet" }).click();
  await expect(panel.getByRole("alert")).toContainText("Hiçbir plan değiştirilmedi"); expect(state.saved).toBeNull();
});
