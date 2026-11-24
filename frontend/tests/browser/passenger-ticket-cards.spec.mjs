import { test, expect } from "@playwright/test";
import { buildSeats } from "../../src/seatPlans.ts";
import { chooseSeat, installSeatSelectionMock } from "./helpers/seat-selection.mjs";
const profile = { fullName: "Hesap Yolcusu", identityNumber: "10000000146", email: "buyer@example.test", phoneNumber: "+905321234567" };
const orderId = "12345678-1234-1234-1234-123456789012";
async function setup(page, { sun = { side: "Left", message: "Bu seferde güneş çoğunlukla sol taraftadır.", leftMinutes: 300, rightMinutes: 20, nightMinutes: 40 }, profileStatus = 200 } = {}) {
  const state = { purchases: [], quotes: [], holds: [] };
  page.on("response", async response => { if (response.url().includes("/api/seat-selections") && response.ok()) { const data = await response.json().catch(() => null); if (data?.expiresAt && data.phase === "Selection") state.holds.push(data.expiresAt); } });
  await page.addInitScript(() => { sessionStorage.setItem("travela_token", "ticket-card-ui"); sessionStorage.setItem("travela_user", JSON.stringify({ role: "Passenger", fullName: "Hesap Yolcusu" })); });
  await page.route("**/api/**", async route => {
    const request = route.request(), path = new URL(request.url()).pathname;
    if (path === "/api/profile") return route.fulfill({ status: profileStatus, json: profile });
    if (path === "/api/trips/public/12") return route.fulfill({ json: { id: 12, companyName: "Travela Seyahat", departureCity: "İstanbul", arrivalCity: "Ankara", departureTime: "2099-06-21T07:00:00Z", arrivalTime: "2099-06-21T13:00:00Z", durationMinutes: 360, price: 439.50, availableSeats: 41, seatCapacity: 41, seatLayout: "2+1", seats: buildSeats("2+1", 41), occupiedSeats: [], seatOccupancies: [], status: "Scheduled", canReserve: true, seatPlanAvailable: true, sunExposure: sun } });
    if (path === "/api/purchases/options") return route.fulfill({ json: { provider: "LocalTest", requiresBuyer: false } });
    if (path === "/api/campaigns/quote") {
      const body = request.postDataJSON(); state.quotes.push(body); const originalAmount = body.passengerCount * 439.50, discountAmount = body.couponCode === "KART100" ? 100 : 60;
      return route.fulfill({ json: { originalAmount, discountAmount, amount: originalAmount - discountAmount, campaignName: body.couponCode ? "Bilet indirimi" : "Otomatik kampanya", couponCode: body.couponCode } });
    }
    if (path === "/api/purchases") { state.purchases.push(request.postDataJSON()); return route.fulfill({ json: { id: orderId } }); }
    if (path === `/api/purchases/${orderId}`) return route.fulfill({ json: { id: orderId, amount: 779, provider: "LocalTest", isTest: true, paymentStatus: "Pending", checkoutState: "Ready", legalAccepted: true, tickets: [], holdExpiresAt: "2099-06-21T06:59:00Z" } });
    return route.fulfill({ json: [] });
  });
  await installSeatSelectionMock(page); await page.goto("/trips/12"); return state;
}
const field = (page, label, seat) => page.getByLabel(`${label} · Koltuk ${seat}`, { exact: true });
const card = (page, seat) => page.getByRole("group", { name: `Yolcu Bilgileri — Koltuk ${seat}`, exact: true });
for (const width of [390, 768, 1440]) test(`Yatay yolcu kartları, orta kartı kaldırma ve ödeme: ${width}px`, async ({ page }) => {
  await page.setViewportSize({ width, height: 1000 }); const state = await setup(page);
  await expect(page.getByText("Devam etmek için otobüs krokisinden koltuk seçin", { exact: true })).toBeVisible(); await expect(page.locator(".passenger-ticket-card")).toHaveCount(0);
  const first = await chooseSeat(page, 8); await expect(first).toBeFocused(); await expect(card(page, 8)).toBeVisible();
  await expect(field(page, "Ad soyad", 8)).toHaveValue(profile.fullName); await expect(card(page, 8).locator(".passenger-ticket-stub")).toContainText("439,50");
  await chooseSeat(page, 2); await field(page, "Ad soyad", 2).fill("Ortadaki Yolcu"); await field(page, "T.C. Kimlik No", 2).fill("12345678950");
  await chooseSeat(page, 5); await field(page, "Ad soyad", 5).fill("Üçüncü Yolcu"); await field(page, "T.C. Kimlik No", 5).fill("12345678950");
  await expect(page.getByRole("heading", { name: "Yolcu bilgileri · 3 yolcu", exact: true })).toBeVisible();
  await expect(field(page, "Ad soyad", 8)).toHaveValue(profile.fullName); await expect(field(page, "Ad soyad", 2)).toHaveValue("Ortadaki Yolcu");
  await expect(page.getByLabel("İletişim e-posta", { exact: true })).toHaveCount(1); await expect(page.getByLabel("İletişim telefon", { exact: true })).toHaveCount(1);
  const timerBefore = await page.getByRole("timer").innerText();
  const geometry = await page.evaluate(() => {
    const rect = selector => { const r = document.querySelector(selector).getBoundingClientRect(); return { x: r.x, y: r.y, width: r.width, height: r.height, right: r.right, bottom: r.bottom }; };
    return { summary: rect(".trip-summary-card"), plan: rect(".seat-booking-plan"), cards: rect(".passenger-ticket-list"), contact: rect(".checkout-contact"), coupon: rect(".coupon-checkout"), total: rect(".passenger-checkout-total"), left: rect(".passenger-ticket-journey"), middle: rect(".passenger-ticket-passenger"), stub: rect(".passenger-ticket-stub"), email: rect('[aria-label="İletişim e-posta"]'), phone: rect('[aria-label="İletişim telefon"]') };
  });
  for (const [a, b] of [["summary", "plan"], ["plan", "cards"], ["cards", "contact"], ["coupon", "total"]]) expect(geometry[a].bottom).toBeLessThanOrEqual(geometry[b].y + 1);
  if (width > 900) { expect(geometry.contact.right).toBeLessThanOrEqual(geometry.coupon.x); expect(geometry.coupon.width).toBeGreaterThan(400); } else expect(geometry.contact.bottom).toBeLessThanOrEqual(geometry.coupon.y);
  const main = await page.locator("main").boundingBox(); expect(main.width).toBeLessThanOrEqual(1180);
  expect(Math.abs(geometry.cards.width - geometry.plan.width)).toBeLessThanOrEqual(1);
  if (width > 1000) { expect(geometry.left.right).toBeLessThanOrEqual(geometry.middle.x + 1); expect(geometry.middle.right).toBeLessThanOrEqual(geometry.stub.x + 1); }
  else { expect(geometry.left.bottom).toBeLessThanOrEqual(geometry.middle.y + 1); expect(geometry.middle.bottom).toBeLessThanOrEqual(geometry.stub.y + 1); }
  if (geometry.contact.width >= 420) expect(Math.abs(geometry.email.y - geometry.phone.y)).toBeLessThanOrEqual(1); else expect(geometry.email.bottom).toBeLessThanOrEqual(geometry.phone.y);
  await expect(page.locator(".seat-booking > aside")).toHaveCount(0); await expect(page.locator(".checkout-confirmation")).toHaveCSS("flex-direction", "row");
  await page.getByRole("button", { name: "Koltuğu çıkar · 2", exact: true }).click(); await expect(card(page, 2)).toHaveCount(0);
  await expect(field(page, "Ad soyad", 8)).toHaveValue(profile.fullName); await expect(field(page, "Ad soyad", 5)).toHaveValue("Üçüncü Yolcu"); await expect(field(page, "T.C. Kimlik No", 5)).toHaveValue("12345678950");
  await page.getByRole("button", { name: "Başka yolcu için bilgileri temizle · Koltuk 5", exact: true }).click(); await expect(field(page, "Ad soyad", 5)).toHaveValue("");
  await page.getByRole("button", { name: "Hesap bilgilerimi kullan · Koltuk 5", exact: true }).click(); await expect(field(page, "Ad soyad", 5)).toHaveValue(profile.fullName);
  await field(page, "Ad soyad", 5).fill("Son Yolcu");
  await chooseSeat(page, 8, "Erkek"); await expect(field(page, "Ad soyad", 8)).toHaveValue(profile.fullName); await expect(field(page, "Ad soyad", 5)).toHaveValue("Son Yolcu");
  await expect(page.getByLabel("Yolcu cinsiyeti · Koltuk 8")).toContainText("Erkek"); await expect(page.getByLabel("Yolcu cinsiyeti · Koltuk 5")).toContainText("Kadın");
  await page.getByLabel("Kupon kodunuz var mı?").fill("KART100"); await page.getByRole("button", { name: "Uygula", exact: true }).click();
  const total = page.getByRole("region", { name: "Fiyat özeti", exact: true }); await expect(total.locator(".campaign-total")).toContainText("779,00"); await expect(total.locator(".campaign-saving")).toContainText("100,00");
  for (const seat of [8, 5]) { await expect(card(page, seat).locator(".passenger-ticket-stub")).toContainText("İndirim öncesi bilet tutarı"); await expect(card(page, seat).locator(".passenger-ticket-stub")).toContainText("439,50"); await expect(card(page, seat)).not.toContainText(/PNR|QR|barkod|Biletiniz oluşturuldu/); }
  expect(timerBefore).toContain("Kalan süre");
  await expect(page.locator(".passenger-heading-row").getByRole("timer")).toHaveAttribute("aria-live", "off");
  expect(new Set(state.holds).size).toBe(1); await expect(page.getByRole("timer")).toBeVisible();
  await field(page, "T.C. Kimlik No", 5).fill("12345678901"); await field(page, "Ad soyad", 5).focus(); await expect(card(page, 5).getByText("Geçerli bir T.C. kimlik numarası giriniz.", { exact: true })).toBeVisible(); await expect(field(page, "T.C. Kimlik No", 5)).toHaveAttribute("aria-invalid", "true");
  await page.getByRole("checkbox", { name: /Mesafeli satış sözleşmesini ve ön bilgilendirme/ }).check(); await page.getByRole("checkbox", { name: /Yolcu bilgilerini kontrol ettim/ }).check();
  await page.getByRole("button", { name: "Ödemeye devam et", exact: true }).click(); expect(state.purchases).toHaveLength(0);
  await field(page, "T.C. Kimlik No", 5).fill("12345678950"); await field(page, "Ad soyad", 5).focus();
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width + 1);
  const fit = await page.locator(".passenger-ticket-card").evaluateAll(cards => cards.every(card => { const box = card.getBoundingClientRect(); return [...card.querySelectorAll("input,button")].every(input => { const r = input.getBoundingClientRect(); return r.x >= box.x && r.right <= box.right + 1; }); })); expect(fit).toBe(true);
  expect(await page.evaluate(() => [...Object.values(localStorage), ...Object.values(sessionStorage)].some(value => value.includes("12345678950") || value.includes("10000000146") || value.includes("buyer@example.test")))).toBe(false);
  await page.evaluate(() => window.scrollTo(0, 0)); await page.screenshot({ path: `../tmp/compact-booking/booking-${width}.png`, fullPage: true });
  await page.getByRole("button", { name: "Ödemeye devam et", exact: true }).click(); await expect(page).toHaveURL(`/purchase/${orderId}`);
  expect(state.purchases[0]).toMatchObject({ expectedAmount: 779, couponCode: "KART100", contactEmail: profile.email, contactPhone: profile.phoneNumber, passengers: [{ seatNumber: 5, fullName: "Son Yolcu", identityNumber: "12345678950", gender: "Female" }, { seatNumber: 8, fullName: profile.fullName, identityNumber: profile.identityNumber, gender: "Male" }] });
});
for (const width of [390, 768, 1440]) test(`Kompakt araç çubuğu, güneş ve ayırma detayları: ${width}px`, async ({ page }) => {
  await page.setViewportSize({ width, height: 1000 }); await setup(page, { sun: { side: "Unknown", message: "Güzergâh geometrisi bulunmadığından güneş hesaplanamadı.", leftMinutes: 0, rightMinutes: 0, nightMinutes: 0 }, profileStatus: 503 });
  const sun = page.locator(".sun-exposure"); const summary = sun.locator("summary");
  await expect(summary).toContainText("Güneş yönü hesaplanamadı."); await expect(sun.locator(".sun-exposure-details")).toBeHidden();
  await summary.focus(); await page.keyboard.press("Enter"); await expect(sun).toHaveAttribute("open", ""); await expect(sun).toContainText("Güzergâh geometrisi bulunmadığından");
  await page.keyboard.press("Space"); await expect(sun).not.toHaveAttribute("open", ""); await expect(sun.locator(".sun-exposure-details")).toBeHidden();
  const toolbar = page.locator(".seat-map-toolbar"); const refresh = toolbar.getByRole("button", { name: "Koltuk planını yenile", exact: true }); await expect(refresh).toBeVisible();
  if (width === 1440) { const legend = await toolbar.locator(".physical-seat-legend").boundingBox(), button = await refresh.boundingBox(); expect(legend.x + legend.width).toBeLessThanOrEqual(button.x); expect(Math.abs(legend.y + legend.height / 2 - button.y - button.height / 2)).toBeLessThanOrEqual(1); }
  await expect(page.getByRole("timer")).toHaveCount(0); await chooseSeat(page, 8);
  await expect(page.getByRole("heading", { name: "Yolcu bilgileri · 1 yolcu", exact: true })).toBeVisible();
  await expect(page.locator(".passenger-heading-row").getByRole("timer")).toBeVisible(); await expect(page.getByText("Hesap bilgileri alınamadı. Bilgileri elle doldurun.", { exact: true })).toBeVisible();
  const help = page.locator(".booking-help"); await expect(help.locator("div")).toBeHidden(); await help.locator("summary").focus(); await page.keyboard.press("Enter"); await expect(help.locator("div")).toBeVisible(); await expect(help).toContainText("Biletiniz ödeme doğrulandığında oluşur."); await page.keyboard.press("Enter"); await expect(help.locator("div")).toBeHidden();
  const main = await page.locator("main").boundingBox(); expect(main.width).toBeLessThanOrEqual(1180); if (width === 1440) expect(Math.abs(main.x - (width - main.width) / 2)).toBeLessThanOrEqual(1);
  await field(page, "Ad soyad", 8).fill("A"); await field(page, "T.C. Kimlik No", 8).focus(); await expect(card(page, 8).getByText("Ad soyad en az iki karakter olmalıdır.", { exact: true })).toBeVisible();
  await field(page, "T.C. Kimlik No", 8).fill("12345678901"); await field(page, "Ad soyad", 8).focus(); await expect(field(page, "T.C. Kimlik No", 8)).toHaveAttribute("aria-invalid", "true");
  const size = await card(page, 8).boundingBox(); expect(size.height).toBeLessThan(width > 1000 ? 320 : 610);
  const inputsFit = await card(page, 8).evaluate(element => { const box = element.getBoundingClientRect(); return [...element.querySelectorAll("input,button")].every(input => { const r = input.getBoundingClientRect(); return r.x >= box.x && r.right <= box.right + 1; }); }); expect(inputsFit).toBe(true);
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width + 1);
  await page.evaluate(() => window.scrollTo(0, 0)); await page.screenshot({ path: `../tmp/compact-booking/errors-${width}.png`, fullPage: true });
});
