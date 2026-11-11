import { test, expect } from "@playwright/test";
import { chooseSeat, releaseSeat } from "./helpers/seat-selection.mjs";
import { buildSeats } from "../../src/seatPlans.ts";
import { LEGAL_VERSION } from "../../src/legal.ts";

const salesLabel = "Mesafeli satış sözleşmesini ve ön bilgilendirme formunu okudum, kabul ediyorum";
const groupId = "12345678-1234-1234-1234-123456789012";

for (const [path, title] of [
  ["/hakkimizda", "Hakkımızda"],
  ["/iletisim", "İletişim"],
  ["/yardim", "Yardım ve Sıkça Sorulan Sorular"],
  ["/gizlilik-ve-kvkk", "Gizlilik ve KVKK Aydınlatma Metni"],
  ["/kullanim-kosullari", "Kullanım Koşulları"],
  ["/mesafeli-satis-sozlesmesi", "Mesafeli Satış Sözleşmesi ve Ön Bilgilendirme Formu"],
  ["/iade-ve-iptal", "İade ve İptal Koşulları"],
  ["/cerez-politikasi", "Çerez Politikası"],
]) {
  test(`Taslak sayfa başlık, tarih ve inceleme notuyla açılır: ${path}`, async ({ page }) => {
    await page.goto(path);
    await expect(page.getByRole("heading", { name: title, exact: true })).toBeVisible();
    await expect(page.getByRole("note")).toContainText("Hukuki inceleme gerektirir");
    await expect(page.locator(".legal-page-meta")).toContainText(LEGAL_VERSION);
    await expect(page.getByRole("contentinfo")).toHaveCount(1);
  });
}

test("Kayıt iki onay olmadan ilerlemez; sürümlü onaylar isteğe eklenir", async ({ page }) => {
  let sent;
  await page.route("**/api/auth/register", route => {
    sent = route.request().postDataJSON();
    return route.fulfill({ status: 201, json: { requiresActivation: true } });
  });
  await page.goto("/register");
  await page.getByLabel("Ad soyad", { exact: true }).fill("Onay Test Yolcusu");
  await page.getByLabel("E-posta", { exact: true }).fill("consent@example.test");
  const testPassword = crypto.randomUUID() + "Aa1!";
  await page.getByLabel("Şifre", { exact: true }).fill(testPassword);
  await page.getByLabel("Şifre Tekrar", { exact: true }).fill(testPassword);
  const submit = page.getByRole("button", { name: "Kayıt Ol", exact: true });
  await expect(submit).toBeDisabled();
  await page.getByRole("checkbox", { name: "Gizlilik ve KVKK aydınlatma metnini okudum" }).check();
  await expect(submit).toBeDisabled();
  expect(sent).toBeUndefined();
  await page.getByRole("checkbox", { name: "Kullanım koşullarını kabul ediyorum" }).check();
  await submit.click();
  await expect(page).toHaveURL(/\/resend-activation$/);
  expect(sent.legalAcceptances).toEqual([
    { documentType: "PrivacyNotice", version: LEGAL_VERSION, accepted: true },
    { documentType: "TermsOfUse", version: LEGAL_VERSION, accepted: true },
  ]);
});

async function passengerSession(page) {
  await page.addInitScript(() => {
    sessionStorage.setItem("travela_token", "isolated-consent-test-session");
    sessionStorage.setItem("travela_user", JSON.stringify({ role: "Passenger" }));
  });
}

test("Koltuk onayı sözleşme olmadan sipariş oluşturmaz; seçim değişirse yeniden onay ister", async ({ page }) => {
  await passengerSession(page);
  let sent;
  await page.route("**/api/profile", r => r.fulfill({ status: 404, json: {} }));
  await page.route("**/api/purchases/options", r => r.fulfill({ json: { provider: "LocalTest", requiresBuyer: false } }));
  await page.route("**/api/trips/public/12", r => r.fulfill({ json: {
    id: 12, companyName: "Test Firma", departureCity: "İzmir", arrivalCity: "Ankara",
    departureTime: "2099-11-10T07:00:00Z", arrivalTime: "2099-11-10T13:00:00Z",
    durationMinutes: 360, price: 500, availableSeats: 9, seatCapacity: 9,
    status: "Scheduled", canReserve: true, seatPlanAvailable: true,
    seatLayout: "2+1", seats: buildSeats("2+1", 9), occupiedSeats: [],
  } }));
  await page.route("**/api/campaigns/quote", r => {
    const amount = r.request().postDataJSON().passengerCount * 500;
    return r.fulfill({ json: { amount, originalAmount: amount, discountAmount: 0 } });
  });
  await page.route("**/api/purchases", r => {
    sent = r.request().postDataJSON();
    return r.fulfill({ status: 409, json: { message: "Test: istek alındı." } });
  });
  await page.goto("/trips/12");
  await chooseSeat(page, 2);
  await page.getByLabel("Ad soyad · Koltuk 2").fill("Onay Test Yolcusu");
  await page.getByLabel("E-posta · Koltuk 2").fill("consent@example.test");
  await page.getByLabel("Telefon · Koltuk 2").fill("+905321234567");
  await page.getByRole("checkbox", { name: /Yolcu bilgilerini kontrol ettim/ }).check();
  const submit = page.getByRole("button", { name: "Ödemeye geç", exact: true });
  await expect(submit).toBeDisabled();
  expect(sent).toBeUndefined();
  await page.getByRole("checkbox", { name: salesLabel }).check();
  await expect(submit).toBeEnabled();
  await chooseSeat(page, 3);
  await expect(page.getByRole("checkbox", { name: salesLabel })).not.toBeChecked();
  await expect(submit).toBeDisabled();
  await releaseSeat(page, 3);
  await page.getByRole("checkbox", { name: salesLabel }).check();
  await submit.click();
  await expect.poll(() => sent?.legalAcceptances).toEqual([
    { documentType: "DistanceSales", version: LEGAL_VERSION, accepted: true },
  ]);
});

test("Eski bekleyen sipariş ödeme bağlantısını açık onay kaydedilene kadar sunmaz", async ({ page }) => {
  await passengerSession(page);
  let accepted = false;
  const group = () => ({
    id: groupId, amount: 500, provider: "IyzicoSandbox", isTest: true,
    paymentStatus: "Pending", checkoutState: "Ready", legalAccepted: accepted,
    checkoutUrl: "https://sandbox-cpp.iyzipay.com/?token=isolated-ui-fixture",
    holdExpiresAt: "2099-11-10T06:59:00Z", refundRequested: 0, refunded: 0, tickets: [],
  });
  await page.route(`**/api/purchases/${groupId}`, r => r.fulfill({ json: group() }));
  await page.route(`**/api/purchases/${groupId}/legal-acceptance`, r => {
    expect(r.request().postDataJSON().legalAcceptances[0].version).toBe(LEGAL_VERSION);
    accepted = true;
    return r.fulfill({ json: group() });
  });
  await page.goto(`/purchase/${groupId}`);
  const paymentLink = page.getByRole("link", { name: "iyzico sandbox ödeme sayfasına git" });
  await expect(paymentLink).toHaveCount(0);
  const continueButton = page.getByRole("button", { name: "Onayla ve ödemeye devam et" });
  await expect(continueButton).toBeDisabled();
  await page.getByRole("checkbox", { name: salesLabel }).check();
  await continueButton.click();
  await expect(paymentLink).toBeVisible();
});

test("Eski iade URL'si güncel sayfaya yönlendirilir", async ({ page }) => {
  await page.goto("/iade-ve-iptal-kosullari");
  await expect(page).toHaveURL(/\/iade-ve-iptal$/);
  await expect(page.getByRole("heading", { name: "İade ve İptal Koşulları", exact: true })).toBeVisible();
});
