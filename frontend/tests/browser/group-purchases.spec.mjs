import { test, expect } from "@playwright/test";
import { buildSeats } from "../../src/seatPlans.ts";
import { chooseSeat } from "./helpers/seat-selection.mjs";
const id = "12345678-1234-1234-1234-123456789012";
async function setup(page, provider = "LocalTest", width = 1440) {
  await page.setViewportSize({ width, height: 1000 });
  await page.addInitScript(() => { sessionStorage.setItem("travela_token", "group-token"); sessionStorage.setItem("travela_user", JSON.stringify({ role: "Passenger", fullName: "Hesap Sahibi" })); });
  await page.route("**/api/profile", r => r.fulfill({ json: { fullName: "Hesap Sahibi", email: "buyer@example.com", phoneNumber: "+905321234567", identityNumber: "10000000146" } }));
  await page.route("**/api/purchases/options", r => r.fulfill({ json: { provider, requiresBuyer: provider === "IyzicoSandbox" } }));
  await page.route("**/api/campaigns/quote", r => {
    const count = r.request().postDataJSON().passengerCount;
    return r.fulfill({ json: { amount: 333.33 * count, originalAmount: 333.33 * count, discountAmount: 0 } });
  });
  await page.route("**/api/trips/public/12", r => r.fulfill({ json: { id: 12, companyName: "Test Firma", departureCity: "İzmir", arrivalCity: "Ankara", departureTime: "2099-11-10T07:00:00Z", arrivalTime: "2099-11-10T13:00:00Z", durationMinutes: 360, price: 333.33, availableSeats: 7, seatCapacity: 9, status: "Scheduled", canReserve: true, seatPlanAvailable: true, seatLayout: "2+1", seats: buildSeats("2+1", 9, [9]), occupiedSeats: [1] } }));
}
const ticket = { id, purchaseGroupId: id, pnr: null, kind: "Hold", tripId: 12, seatNumber: 2, companyName: "Test Firma", passengerName: "Başka Yolcu", passengerEmail: "traveler@example.com", departureCity: "İzmir", arrivalCity: "Ankara", departureTime: "2099-11-10T07:00:00Z", arrivalTime: "2099-11-10T13:00:00Z", cancelDeadline: "2099-11-10T05:00:00Z", holdExpiresAt: "2099-11-10T06:59:00Z", amount: 333.33, currency: "TRY", isTest: true, paymentStatus: "Pending", ticketStatus: "Active", refundStatus: "None", refundAmount: 0, canCancel: false, retrievedAt: "2026-11-04T07:00:00Z", checkoutUrl: `/purchase/${id}` };
const group = { id, amount: 666.66, currency: "TRY", provider: "LocalTest", isTest: true, legalAccepted: true, paymentStatus: "Pending", checkoutState: "Ready", holdExpiresAt: ticket.holdExpiresAt, checkoutUrl: `/purchase/${id}`, refundRequested: 0, refunded: 0, tickets: [ticket, { ...ticket, id: "22345678-1234-1234-1234-123456789012", seatNumber: 3, passengerName: "İkinci Yolcu" }] };
for (const width of [390,1440]) test(`İki yolcu kendi bilgileriyle ortak siparişe geçer: ${width}px`, async ({ page }) => {
  await setup(page, "LocalTest", width); let sent;
  await page.route("**/api/purchases", async r => { sent = r.request().postDataJSON(); await r.fulfill({ json: group }); }); await page.route(`**/api/purchases/${id}`, r => r.fulfill({ json: group }));
  await page.goto("/trips/12");
  await chooseSeat(page, 2, "Kadın");
  await chooseSeat(page, 3, "Erkek");
  for (const [seat, name] of [[2, "Başka Yolcu"], [3, "İkinci Yolcu"]]) {
    await page.getByLabel(`Ad soyad · Koltuk ${seat}`).fill(name);
    await page.getByLabel(`T.C. Kimlik No · Koltuk ${seat}`).fill("10000000146");
  }
  await expect(page.getByRole("group", { name: "İletişim Bilgileri", exact: true }).locator("input")).toHaveCount(2);
  await expect(page.getByRole("group", { name: "Yolcu Bilgileri — Koltuk 2", exact: true }).locator("input")).toHaveCount(2);
  const formSize = await page.evaluate(() => ({ width: document.documentElement.scrollWidth, viewport: innerWidth }));
  expect(formSize.width).toBeLessThanOrEqual(formSize.viewport + 1);
  await page.screenshot({ path: `../tmp/ticket-qa/shared-contact-form-${width}.png`, fullPage: true });
  await page.getByRole("checkbox", { name: /Yolcu bilgilerini kontrol ettim/ }).check();
  await page.getByRole("checkbox", { name: /Mesafeli satış sözleşmesini ve ön bilgilendirme/ }).check();
  await page.getByRole("button", { name: "Ödemeye geç", exact: true }).click();
  await expect(page).toHaveURL(`/purchase/${id}`);
  expect(sent.contactEmail).toBe("buyer@example.com"); expect(sent.contactPhone).toBe("+905321234567");
  expect(sent.passengers.every(p => p.identityNumber === "10000000146" && !("email" in p) && !("phone" in p))).toBe(true);
  expect(sent.passengers).toHaveLength(2); expect(sent.passengers[0].fullName).toBe("Başka Yolcu"); expect(sent.buyer).toBeUndefined();
  expect(sent.passengers.map(({ seatNumber, gender }) => ({ seatNumber, gender }))).toEqual([
    { seatNumber: 2, gender: "Female" }, { seatNumber: 3, gender: "Male" },
  ]);
  await expect(page.getByRole("heading", { name: /2 yolcu/ })).toContainText("666,66"); await expect(page.getByText("İkinci Yolcu", { exact: true })).toBeVisible();
  const size = await page.evaluate(() => ({ width: document.documentElement.scrollWidth, viewport: innerWidth })); expect(size.width).toBeLessThanOrEqual(size.viewport + 1);
  await page.screenshot({ path: `../tmp/ticket-qa/group-purchase-${width}.png`, fullPage: true });
});
test("iyzico sandbox akışı alıcı bilgilerini toplar ve yalnızca sağlayıcıya yönlendirir", async ({ page }) => {
  await setup(page, "IyzicoSandbox"); let sent; const sandboxGroup = { ...group, provider: "IyzicoSandbox", checkoutUrl: "https://sandbox-cpp.iyzipay.com/?token=test-sandbox" };
  await page.route("**/api/purchases", async r => { sent = r.request().postDataJSON(); await r.fulfill({ json: sandboxGroup }); }); await page.route(`**/api/purchases/${id}`, r => r.fulfill({ json: sandboxGroup }));
  await page.goto("/trips/12");
  await chooseSeat(page, 2);
  await page.getByRole("button", { name: "Hesap bilgilerimi kullan · Koltuk 2" }).click();
  for (const [field,value] of Object.entries({ name:"Alıcı", surname:"Test", identityNumber:"11111111111", address:"Sandbox adresi", city:"İzmir", country:"Türkiye", zipCode:"35000" })) await page.getByLabel(`Alıcı ${field}`, { exact: true }).fill(value);
  await page.getByRole("checkbox", { name: /Yolcu bilgilerini kontrol ettim/ }).check();
  await page.getByRole("checkbox", { name: /Mesafeli satış sözleşmesini ve ön bilgilendirme/ }).check();
  await page.getByRole("button", { name: "Ödemeye geç", exact: true }).click();
  await expect(page).toHaveURL(`/purchase/${id}`);
  expect(sent.buyer.identityNumber).toBe("11111111111"); expect(sent.passengers[0].gender).toBe("Female"); await expect(page.getByRole("link", { name: "iyzico sandbox ödeme sayfasına git" })).toHaveAttribute("href", sandboxGroup.checkoutUrl);
  expect(sent.buyer.phone).toBe(sent.contactPhone);
  await expect(page.getByRole("button", { name: "Ödemeyi başarılı yap" })).toHaveCount(0); await expect(page.locator('input[autocomplete="cc-number"]')).toHaveCount(0);
});

test("Koltuk ekleme, cinsiyet değiştirme ve çıkarma diğer yolcuyu ve ortak iletişimi korur", async ({ page }) => {
  await setup(page);
  await page.goto("/trips/12");
  await chooseSeat(page, 2);
  await page.getByRole("button", { name: "Hesap bilgilerimi kullan · Koltuk 2" }).click();
  await expect(page.getByLabel("T.C. Kimlik No · Koltuk 2")).toHaveValue("10000000146");
  await page.getByLabel("İletişim e-posta").fill("edited@example.com");
  await page.getByLabel("İletişim telefon").fill("+905551112233");
  await chooseSeat(page, 3, "Erkek");
  await page.getByLabel("Ad soyad · Koltuk 3").fill("İkinci Yolcu");
  await page.getByLabel("T.C. Kimlik No · Koltuk 3").fill("10000000146");
  await chooseSeat(page, 2, "Erkek");
  await expect(page.getByLabel("Ad soyad · Koltuk 2")).toHaveValue("Hesap Sahibi");
  await page.getByRole("button", { name: "Koltuğu çıkar · 2", exact: true }).click();
  await expect(page.getByLabel("Ad soyad · Koltuk 3")).toHaveValue("İkinci Yolcu");
  await expect(page.getByLabel("T.C. Kimlik No · Koltuk 3")).toHaveValue("10000000146");
  await chooseSeat(page, 2);
  await expect(page.getByLabel("Ad soyad · Koltuk 3")).toHaveValue("İkinci Yolcu");
  await expect(page.getByLabel("İletişim e-posta")).toHaveValue("edited@example.com");
  await expect(page.getByLabel("İletişim telefon")).toHaveValue("+905551112233");
  await page.getByRole("button", { name: "Koltuğu çıkar · 2", exact: true }).click();
  await page.getByRole("button", { name: "Koltuğu çıkar · 3", exact: true }).click();
  await chooseSeat(page, 2);
  await expect(page.getByLabel("İletişim e-posta")).toHaveValue("edited@example.com");
});

test("Tek yolcuda eksik/geçersiz bilgiler gönderilmez; düzeltilen bilgilerle ödeme tamamlanır", async ({ page }) => {
  await setup(page, "LocalTest", 390); let sent;
  const single = { ...group, amount: 333.33, tickets: [ticket] };
  await page.route("**/api/purchases", r => { sent = r.request().postDataJSON(); return r.fulfill({ json: single }); });
  await page.route(`**/api/purchases/${id}`, r => r.fulfill({ json: single }));
  await page.route(`**/api/purchases/${id}/local-complete`, r => r.fulfill({ json: { ...single, paymentStatus: "Paid", tickets: [{ ...ticket, kind: "Ticket", pnr: "A".repeat(32), paymentStatus: "Paid" }] } }));
  await page.goto("/trips/12");
  await chooseSeat(page, 2);
  await page.getByRole("checkbox", { name: /Yolcu bilgilerini kontrol ettim/ }).check();
  await page.getByRole("checkbox", { name: /Mesafeli satış sözleşmesini ve ön bilgilendirme/ }).check();
  const submit = page.getByRole("button", { name: "Ödemeye geç", exact: true });
  await submit.click(); expect(sent).toBeUndefined();
  await page.getByLabel("Ad soyad · Koltuk 2").fill("Tek Yolcu");
  for (const invalid of ["1000000014", "00000000000", "11111111111", "10000000147"]) {
    await page.getByLabel("T.C. Kimlik No · Koltuk 2").fill(invalid);
    await submit.click(); expect(sent).toBeUndefined();
  }
  // Autofill must clear any previous custom validity error.
  await page.getByRole("button", { name: "Hesap bilgilerimi kullan · Koltuk 2" }).click();
  await page.getByLabel("İletişim e-posta").fill("invalid");
  await submit.click(); expect(sent).toBeUndefined();
  await page.getByLabel("İletişim e-posta").fill("shared@example.com");
  await page.getByLabel("İletişim telefon").fill("123");
  await submit.click(); expect(sent).toBeUndefined();
  await page.getByLabel("İletişim telefon").fill("+905551112233");
  await submit.click();
  await expect(page).toHaveURL(`/purchase/${id}`);
  expect(sent.passengers).toHaveLength(1);
  expect(sent.contactEmail).toBe("shared@example.com");
  await page.getByRole("button", { name: "Ödemeyi başarılı yap" }).click();
  await expect(page.locator(".booking-summary").getByText("Ödeme doğrulandı", { exact: true })).toBeVisible();
});

test("Geç gelen profil ortak iletişim düzenlemelerini ezmez; kayıtlı kimlik yoksa girilmiş kimlik korunur", async ({ page }) => {
  await setup(page);
  let release;
  const gate = new Promise(resolve => { release = resolve; });
  await page.route("**/api/profile", async r => { await gate; await r.fulfill({ json: { fullName: "Hesap Sahibi", email: "buyer@example.com", phoneNumber: "+905321234567" } }); });
  await page.goto("/trips/12");
  await chooseSeat(page, 2);
  await page.getByLabel("İletişim e-posta").fill("typed@example.com");
  await page.getByLabel("İletişim telefon").fill("+905551112233");
  await page.getByLabel("T.C. Kimlik No · Koltuk 2").fill("10000000146");
  release();
  await page.getByRole("button", { name: "Hesap bilgilerimi kullan · Koltuk 2" }).click();
  await expect(page.getByLabel("T.C. Kimlik No · Koltuk 2")).toHaveValue("10000000146");
  await expect(page.getByLabel("İletişim e-posta")).toHaveValue("typed@example.com");
  await expect(page.getByLabel("İletişim telefon")).toHaveValue("+905551112233");
});
