import { test, expect } from "@playwright/test";
import { buildSeats } from "../../src/seatPlans.ts";
import { chooseSeat, installSeatSelectionMock } from "./helpers/seat-selection.mjs";

const id = "12345678-1234-1234-1234-123456789012";
const ticket = { id, purchaseGroupId: null, pnr: "A".repeat(32), kind: "Ticket", tripId: 12, seatNumber: 2,
  companyName: "Misafir Firma", passengerName: "Misafir Yolcu", passengerIdentityNumber: "1********46", passengerEmail: "g***@example.test", passengerPhone: "*******4567",
  departureCity: "İzmir", arrivalCity: "Ankara", departureTime: "2099-12-17T07:00:00Z", arrivalTime: "2099-12-17T13:00:00Z", cancelDeadline: "2099-12-17T05:00:00Z",
  holdExpiresAt: "2099-12-17T06:59:00Z", amount: 300, originalAmount: 300, discountAmount: 0, currency: "TRY", isTest: true,
  paymentStatus: "Paid", ticketStatus: "Active", refundStatus: "None", refundAmount: 0, canCancel: true, retrievedAt: "2026-11-23T07:00:00Z", checkoutUrl: null };
const group = { id, guest: true, amount: 300, originalAmount: 300, discountAmount: 0, provider: "LocalTest", isTest: true, legalAccepted: true,
  paymentStatus: "Pending", checkoutState: "Ready", checkoutUrl: `/purchase/${id}`, holdExpiresAt: ticket.holdExpiresAt, refundRequested: 0, refunded: 0,
  tickets: [{ ...ticket, pnr: null, kind: "Hold", paymentStatus: "Pending" }] };
async function setup(page, width = 1440) {
  await page.setViewportSize({ width, height: 1000 });
  const calls = { orders: [], memberOrders: 0, profileWrites: 0, dropNext: false, count: 1 };
  await page.route("**/api/**", async route => {
    const request = route.request(); const path = new URL(request.url()).pathname;
    if (path === "/api/trips/public/12") return route.fulfill({ json: { id: 12, companyName: "Misafir Firma", departureCity: "İzmir", arrivalCity: "Ankara", departureTime: ticket.departureTime,
      arrivalTime: ticket.arrivalTime, durationMinutes: 360, price: 300, availableSeats: 9, seatCapacity: 9, status: "Scheduled", canReserve: true, seatPlanAvailable: true,
      seatLayout: "2+1", seats: buildSeats("2+1", 9), occupiedSeats: [], seatOccupancies: [] } });
    if (path === "/api/purchases/options") return route.fulfill({ json: { provider: "LocalTest", requiresBuyer: false } });
    if (path === "/api/guest/purchases/resume") return route.fulfill({ status: 401, json: {} });
    if (path === "/api/guest/purchases/session") return route.fulfill({ headers: { "Set-Cookie": "travela_guest_checkout=test; HttpOnly; SameSite=Lax; Path=/api" }, json: { ready: true } });
    if (path.endsWith("/quote")) { calls.count = request.postDataJSON().passengerCount; return route.fulfill({ json: { amount: 300 * calls.count, originalAmount: 300 * calls.count, discountAmount: 0 } }); }
    if (path === "/api/guest/purchases") {
      expect(request.headers().authorization).toBeUndefined();
      calls.orders.push(request.postDataJSON());
      if (calls.dropNext) { calls.dropNext = false; return route.abort("connectionreset"); }
      return route.fulfill({ json: group });
    }
    if (path === "/api/purchases") { calls.memberOrders++; return route.fulfill({ json: group }); }
    if (path === `/api/guest/purchases/${id}`) return route.fulfill({ json: { ...group, amount: 300 * calls.count, tickets: Array.from({ length: calls.count }, (_, n) => ({ ...group.tickets[0], id: `${id}-${n}`, seatNumber: n + 2 })) } });
    if (path.endsWith("/local-complete")) return route.fulfill({ json: { ...group, amount: 300 * calls.count, paymentStatus: "Paid", tickets: Array.from({ length: calls.count }, (_, n) => ({ ...ticket, id: `${id}-${n}`, pnr: String.fromCharCode(65 + n).repeat(32), seatNumber: n + 2 })) } });
    if (path.endsWith("/pdf")) return route.fulfill({ contentType: "application/pdf", body: "%PDF-1.7\nTest" });
    if (path === "/api/auth/login") return route.fulfill({ json: { token: "member-token", user: { fullName: "Hesap Sahibi", role: "Passenger" } } });
    if (path === "/api/profile") { if (request.method() !== "GET") calls.profileWrites++; return route.fulfill({ json: { fullName: "Hesap Sahibi", identityNumber: "10000000146", email: "profile@example.test", phoneNumber: "+905551112233" } }); }
    return route.fulfill({ json: [] });
  });
  return calls;
}
async function fill(page, seats = [2]) {
  for (const seat of seats) { await chooseSeat(page, seat); await page.getByLabel(`Ad soyad · Koltuk ${seat}`, { exact: true }).fill(`Misafir Yolcu ${seat}`); await page.getByLabel(`T.C. Kimlik No · Koltuk ${seat}`, { exact: true }).fill("10000000146"); }
  await page.getByLabel("İletişim e-posta").fill("guest@example.test"); await page.getByLabel("İletişim telefon").fill("0532 123 45 67");
  await page.getByRole("checkbox", { name: /Mesafeli satış sözleşmesini ve ön bilgilendirme/ }).check();
  await page.getByRole("checkbox", { name: /Yolcu bilgilerini kontrol ettim/ }).check();
}
for (const width of [1440, 390]) test(`Misafir ${width === 390 ? "çoklu" : "tek"} yolcu satın alma, sonuç ve PDF · ${width}px`, async ({ page }) => {
  const calls = await setup(page, width); await installSeatSelectionMock(page); await page.goto("/trips/12"); await fill(page, width === 390 ? [2, 3] : [2]);
  await expect(page.getByRole("button", { name: "Giriş yap ve devam et" })).toHaveCount(0);
  await page.getByRole("button", { name: "Ödemeye devam et", exact: true }).click();
  await expect(page).toHaveURL(`/purchase/${id}`); expect(calls.orders).toHaveLength(1); expect(calls.memberOrders).toBe(0);
  expect(calls.orders[0].contactPhone).toBe("+905321234567"); expect(calls.orders[0].passengers).toHaveLength(width === 390 ? 2 : 1);
  await page.getByRole("button", { name: "Ödemeyi başarılı yap", exact: true }).click();
  await expect(page.getByText("Ödeme doğrulandı", { exact: true }).first()).toBeVisible();
  await expect(page.getByText("A".repeat(32), { exact: true })).toBeVisible();
  const download = page.waitForEvent("download"); await page.getByRole("button", { name: "PDF indir", exact: true }).first().click(); await download;
  await expect(page.getByRole("link", { name: "Biletimi Sorgula", exact: true }).first()).toBeVisible();
  await expect(page.getByRole("link", { name: "Bilet detayını gör", exact: true })).toHaveCount(0);
  await expect(page.locator("body")).toHaveJSProperty("scrollWidth", width);
  const storage = await page.evaluate(() => JSON.stringify({ local: { ...localStorage }, session: { ...sessionStorage } }));
  expect(storage).not.toContain("10000000146"); expect(storage).not.toContain("guest@example.test"); expect(storage).not.toContain("travela_guest_checkout");
  await page.evaluate(() => window.scrollTo(0, 0));
  await page.screenshot({ path: `../tmp/guest-ticket-qa/purchase-${width}.png`, fullPage: true });
  await page.reload(); await expect(page.getByRole("heading", { name: /Ortak sipariş/ })).toBeVisible(); expect(calls.orders).toHaveLength(1);
});
test("İsteğe bağlı giriş formu ve koltukları korur; profil elle düzenlenen alanları ezmez", async ({ page }) => {
  const calls = await setup(page, 390); await installSeatSelectionMock(page); await page.goto("/trips/12"); await fill(page, [2, 3]);
  await page.getByRole("button", { name: "Hesabınızla giriş yapın (isteğe bağlı)", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "İsteğe bağlı hesap girişi" }); await expect(dialog).toBeVisible();
  await dialog.getByLabel("E-posta", { exact: true }).fill("member@example.test"); await dialog.getByLabel("Şifre", { exact: true }).fill("TestPass123!");
  await dialog.getByRole("button", { name: "Giriş Yap", exact: true }).click(); await expect(dialog).toHaveCount(0);
  await expect(page.getByLabel("Ad soyad · Koltuk 2", { exact: true })).toHaveValue("Misafir Yolcu 2");
  await expect(page.getByLabel("Ad soyad · Koltuk 3", { exact: true })).toHaveValue("Misafir Yolcu 3");
  await expect(page.getByLabel("İletişim e-posta")).toHaveValue("guest@example.test");
  await expect(page.getByRole("button", { name: /^Koltuk 2,/ })).toHaveAttribute("aria-pressed", "true");
  await expect(page.getByRole("button", { name: "Hesap bilgilerimi kullan · Koltuk 2", exact: true })).toBeVisible(); expect(calls.profileWrites).toBe(0);
});
test("Bağlantı kopunca aynı bilgilerle tekrar gönderim aynı işlem anahtarını kullanır", async ({ page }) => {
  const calls = await setup(page); calls.dropNext = true; await installSeatSelectionMock(page); await page.goto("/trips/12"); await fill(page);
  await page.getByRole("button", { name: "Ödemeye devam et", exact: true }).click(); await expect(page.getByRole("alert")).toContainText("Sunucu yanıtı alınamadı");
  await page.getByRole("button", { name: "Ödemeye devam et", exact: true }).click(); await expect(page).toHaveURL(`/purchase/${id}`);
  expect(calls.orders).toHaveLength(2); expect(calls.orders[0].requestKey).toBe(calls.orders[1].requestKey);
});
test("Mobil sorgu eşleşmeyen bilgiyi açıklamaz; maskeli sonuç, PDF ve kodla iptal gösterir", async ({ page }) => {
  await setup(page, 390); let lookups = 0; let codes = 0;
  await page.route("**/api/ticket-lookup", route => { lookups++; return route.fulfill(lookups === 1 ? { status: 404, json: { message: "Girdiğiniz bilgilerle eşleşen bilet bulunamadı." } } : { json: { ticket } }); });
  await page.route("**/api/ticket-lookup/cancellation-code", route => { codes++; return route.fulfill(codes === 1 ? { status: 503, json: { message: "İptal doğrulama kodu gönderimi şu anda kullanılamıyor." } } : { json: { challengeId: id, destination: "g***@example.test", message: "Doğrulama kodu kabul edildi." } }); });
  await page.route("**/api/ticket-lookup/cancel", route => route.fulfill({ json: { ticket: { ...ticket, canCancel: false, ticketStatus: "Cancelled", refundStatus: "Pending", refundAmount: 300 } } }));
  await installSeatSelectionMock(page); await page.goto("/bilet-sorgula"); await expect(page.getByRole("navigation", { name: "Ana menü" }).getByRole("link", { name: "Biletimi Sorgula" })).toBeVisible();
  await page.getByLabel("PNR numarası", { exact: true }).fill(ticket.pnr); await page.getByLabel("Cep telefonu veya e-posta", { exact: true }).fill("05321234567");
  await page.getByRole("button", { name: "Biletimi Sorgula", exact: true }).click(); await expect(page.getByRole("alert")).toHaveText("Girdiğiniz bilgilerle eşleşen bilet bulunamadı.");
  await page.getByRole("button", { name: "Biletimi Sorgula", exact: true }).click(); await expect(page.getByRole("heading", { name: "Misafir Yolcu", exact: true })).toBeVisible();
  await expect(page.getByText(/1\*{8}46/)).toBeVisible(); await expect(page.getByRole("link", { name: "Ortak sipariş", exact: true })).toHaveCount(0);
  await expect(page.locator("body")).toHaveJSProperty("scrollWidth", 390); await page.screenshot({ path: "../tmp/guest-ticket-qa/lookup-390.png", fullPage: true });
  const send = page.getByRole("button", { name: "İptal için doğrulama kodu gönder", exact: true }); await send.click(); await expect(page.getByRole("alert")).toContainText("kullanılamıyor");
  await expect(page.getByLabel("6 haneli iptal kodu")).toHaveCount(0); await send.click(); await page.getByLabel("6 haneli iptal kodu").fill("123456");
  await page.getByRole("button", { name: "Kodu doğrula ve bileti iptal et", exact: true }).click(); await expect(page.getByText("İPTAL EDİLDİ", { exact: true })).toBeVisible();
  expect(page.url()).not.toContain(ticket.pnr); expect(page.url()).not.toContain("token");
});
