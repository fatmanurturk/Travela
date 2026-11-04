import { test, expect } from "@playwright/test";
async function session(page, role) { await page.addInitScript(role => { sessionStorage.setItem("travela_token", "change-token"); sessionStorage.setItem("travela_user", JSON.stringify({ role, fullName: "Test Kullanıcısı" })); }, role); }
const trip = { id: 12, companyId: 1, companyName: "Test Firma", busId: 4, busPlateNumber: "34 ABC 123", busCapacity: 8, routeId: 3, departureCity: "İstanbul", arrivalCity: "Ankara", departureTime: "2099-01-01T07:00:00Z", arrivalTime: "2099-01-01T13:00:00Z", price: 550, availableSeats: 6, status: "Scheduled", createdAt: "2026-11-04T07:00:00Z", companyIsActive: true, busIsActive: true };
test("Firma satılmış sefer değişikliğini önce önizler, sonra gerekçeyle uygular", async ({ page }) => {
  await session(page, "Company"); await page.setViewportSize({ width: 1440, height: 1100 });
  await page.route("**/api/company/profile", r => r.fulfill({ json: { name: "Test Firma" } }));
  await page.route("**/api/buses?*", r => r.fulfill({ json: { items: [{ id: 4, companyId: 1, plateNumber: "34 ABC 123", seatCapacity: 8, isActive: true }, { id: 5, companyId: 1, plateNumber: "34 XYZ 456", seatCapacity: 8, isActive: true }] } }));
  await page.route("**/api/routes", r => r.fulfill({ json: [{ id: 3, departureCity: "İstanbul", arrivalCity: "Ankara" }] })); await page.route("**/api/terminals", r => r.fulfill({ json: [] }));
  await page.route("**/api/trips?*", r => r.fulfill({ json: { items: [trip], totalCount: 1, totalPages: 1 } })); await page.route("**/api/trips/summary", r => r.fulfill({ json: { totalTrips: 1, scheduledTrips: 1, inProgressTrips: 0, completedTrips: 0, cancelledTrips: 0 } })); await page.route("**/api/trips/12", r => r.fulfill({ json: trip }));
  await page.route("**/api/trips/public/12", r => r.fulfill({ json: { seats: [], seatLayout: "2+2", occupiedSeats: [1, 2], seatPlanAvailable: true } }));
  const sent = []; const before = { ...trip }; const after = { ...trip, departureTime: "2099-01-01T08:00:00Z", arrivalTime: "2099-01-01T14:00:00Z", busPlateNumber: "34 XYZ 456" };
  const change = { id: "change1", version: 1, createdAt: "2026-11-04T09:00:00Z", actorId: 2, reason: "Araç bakımından dolayı değişiklik", beforeJson: JSON.stringify(before), afterJson: JSON.stringify(after), affectedTickets: 2, releasedHolds: 1 };
  await page.route("**/api/trips/12/changes", async r => { if (r.request().method() === "GET") return r.fulfill({ json: { changeVersion: 0, changes: [] } }); const body = r.request().postDataJSON(); sent.push(body); return r.fulfill({ json: body.preview ? { before, after, affectedTickets: 2, releasedHolds: 1, changeVersion: 0, cancellationUntil: after.departureTime } : { message: "Değişiklik kaydedildi.", change } }); });
  await page.goto("/company/trips"); await page.getByRole("button", { name: "Detay", exact: true }).click();
  const editor = page.getByRole("region", { name: "Kontrollü sefer değişikliği" });
  await editor.getByLabel("Yeni araç", { exact: true }).selectOption("5"); await editor.getByLabel("Yeni kalkış (Türkiye)", { exact: true }).fill("2099-01-01T11:00"); await editor.getByLabel("Yeni varış (Türkiye)", { exact: true }).fill("2099-01-01T17:00"); await editor.getByLabel("Değişiklik gerekçesi").fill(change.reason);
  await expect(editor.getByRole("button", { name: "Değişikliği uygula ve bildir" })).toHaveCount(0); await editor.getByRole("button", { name: "Değişikliği önizle" }).click();
  await expect(editor.getByRole("region", { name: "Değişiklik önizlemesi" })).toContainText("2 bilet etkilenecek; 1 ödeme tutması sonlandırılacak");
  await editor.getByRole("button", { name: "Değişikliği uygula ve bildir" }).click(); await expect(editor.getByRole("status")).toContainText("Değişiklik kaydedildi");
  expect(sent).toHaveLength(2); expect(sent[0].preview).toBe(true); expect(sent[1].preview).toBe(false); expect(sent[1].requestKey).toBe(sent[0].requestKey); expect(sent[1].expectedAffectedTickets).toBe(2); expect(sent[1].departureTime).toBe("2099-01-01T08:00:00.000Z");
});

test("Yönetici başarısız bildirimi gerekçeyle yeniden kuyruğa alır", async ({ page }) => {
  await session(page, "Admin"); let retry; const item = { id: "11111111-1111-1111-1111-111111111111", type: "Purchase", channel: "Email", recipient: "passenger@example.com", pnr: "TESTPNR", status: "Failed", attempts: 8, createdAt: "2026-11-04T09:00:00Z", lastError: "Gönderim başarısız." };
  await page.route("**/api/travel-notifications?*", r => r.fulfill({ json: { totalCount: 1, items: [item] } }));
  await page.route(`**/api/travel-notifications/${item.id}`, r => r.fulfill({ json: { message: item, attempts: [{ id: "a1", status: "Failed", createdAt: item.createdAt, error: item.lastError }] } }));
  await page.route(`**/api/travel-notifications/${item.id}/retry`, async r => { retry = r.request().postDataJSON(); await r.fulfill({ json: { message: "Kuyruğa alındı." } }); });
  await page.goto("/admin/notifications"); await expect(page.getByRole("table")).toContainText("Başarısız"); await page.getByRole("button", { name: "Detay", exact: true }).click();
  await page.getByLabel("Yeniden gönderim gerekçesi").fill("SMTP yapılandırması düzeltildi"); await page.getByRole("button", { name: "Yeniden kuyruğa al", exact: true }).click();
  await expect(page.getByRole("region", { name: "Bildirim detayı" })).toHaveCount(0); expect(retry.reason).toBe("SMTP yapılandırması düzeltildi");
});
