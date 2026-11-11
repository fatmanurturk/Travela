import { test, expect } from "@playwright/test";
import { buildSeats } from "../../src/seatPlans.ts";
const id = "12345678-1234-1234-1234-123456789012";
async function setup(page, provider = "LocalTest", width = 1440) {
  await page.setViewportSize({ width, height: 1000 });
  await page.addInitScript(() => { sessionStorage.setItem("travela_token", "group-token"); sessionStorage.setItem("travela_user", JSON.stringify({ role: "Passenger", fullName: "Hesap Sahibi" })); });
  await page.route("**/api/profile", r => r.fulfill({ json: { fullName: "Hesap Sahibi", email: "buyer@example.com", phoneNumber: "+905321234567" } }));
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
  await page.goto("/trips/12"); await page.getByRole("button", { name: /^Koltuk 2,/ }).click(); await page.getByRole("button", { name: /^Koltuk 3,/ }).click();
  for (const [seat,name] of [[2,"Başka Yolcu"],[3,"İkinci Yolcu"]]) { await page.getByLabel(`Ad soyad · Koltuk ${seat}`).fill(name); await page.getByLabel(`E-posta · Koltuk ${seat}`).fill(`traveler${seat}@example.com`); await page.getByLabel(`Telefon · Koltuk ${seat}`).fill("+905321234567"); await page.getByRole("group", { name: `Cinsiyet seçiniz · Koltuk ${seat}` }).getByLabel("Kadın", { exact: true }).check(); }
  await page.getByRole("checkbox", { name: /Yolcu bilgilerini kontrol ettim/ }).check();
  await page.getByRole("checkbox", { name: /Mesafeli satış sözleşmesini ve ön bilgilendirme/ }).check();
  await page.getByRole("button", { name: "Ödemeye geç", exact: true }).click();
  await expect(page).toHaveURL(`/purchase/${id}`);
  expect(sent.passengers).toHaveLength(2); expect(sent.passengers[0].fullName).toBe("Başka Yolcu"); expect(sent.buyer).toBeUndefined();
  await expect(page.getByRole("heading", { name: /2 yolcu/ })).toContainText("666,66"); await expect(page.getByText("İkinci Yolcu", { exact: true })).toBeVisible();
  const size = await page.evaluate(() => ({ width: document.documentElement.scrollWidth, viewport: innerWidth })); expect(size.width).toBeLessThanOrEqual(size.viewport + 1);
  await page.screenshot({ path: `../tmp/ticket-qa/group-purchase-${width}.png`, fullPage: true });
});
test("iyzico sandbox akışı alıcı bilgilerini toplar ve yalnızca sağlayıcıya yönlendirir", async ({ page }) => {
  await setup(page, "IyzicoSandbox"); let sent; const sandboxGroup = { ...group, provider: "IyzicoSandbox", checkoutUrl: "https://sandbox-cpp.iyzipay.com/?token=test-sandbox" };
  await page.route("**/api/purchases", async r => { sent = r.request().postDataJSON(); await r.fulfill({ json: sandboxGroup }); }); await page.route(`**/api/purchases/${id}`, r => r.fulfill({ json: sandboxGroup }));
  await page.goto("/trips/12"); await page.getByRole("button", { name: /^Koltuk 2,/ }).click(); await page.getByRole("button", { name: "Hesap bilgilerimi kullan · Koltuk 2" }).click();
  await page.locator('input[name="gender-2"][value="Female"]').check();
  for (const [field,value] of Object.entries({ name:"Alıcı", surname:"Test", identityNumber:"11111111111", address:"Sandbox adresi", city:"İzmir", country:"Türkiye", zipCode:"35000", phone:"+905321234567" })) await page.getByLabel(`Alıcı ${field}`, { exact: true }).fill(value);
  await page.getByRole("checkbox", { name: /Yolcu bilgilerini kontrol ettim/ }).check();
  await page.getByRole("checkbox", { name: /Mesafeli satış sözleşmesini ve ön bilgilendirme/ }).check();
  await page.getByRole("button", { name: "Ödemeye geç", exact: true }).click();
  await expect(page).toHaveURL(`/purchase/${id}`);
  expect(sent.buyer.identityNumber).toBe("11111111111"); expect(sent.passengers[0].gender).toBe("Female"); await expect(page.getByRole("link", { name: "iyzico sandbox ödeme sayfasına git" })).toHaveAttribute("href", sandboxGroup.checkoutUrl);
  await expect(page.getByRole("button", { name: "Ödemeyi başarılı yap" })).toHaveCount(0); await expect(page.locator('input[autocomplete="cc-number"]')).toHaveCount(0);
});
