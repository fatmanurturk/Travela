import { test, expect } from "@playwright/test";
import { buildSeats } from "../../src/seatPlans.ts";
import { chooseSeat, installSeatSelectionMock } from "./helpers/seat-selection.mjs";

const profile = { fullName: "Hesap Sahibi", identityNumber: "10000000146", email: "buyer@example.test", phoneNumber: "+905321234567" };
const id = "12345678-1234-1234-1234-123456789012";
const name = (page, seat) => page.getByLabel(`Ad soyad · Koltuk ${seat}`, { exact: true });
const identity = (page, seat) => page.getByLabel(`T.C. Kimlik No · Koltuk ${seat}`, { exact: true });
const clear = (page, seat) => page.getByRole("button", { name: `Başka yolcu için bilgileri temizle · Koltuk ${seat}`, exact: true }).click();
const useProfile = (page, seat) => page.getByRole("button", { name: `Hesap bilgilerimi kullan · Koltuk ${seat}`, exact: true }).click();

async function setup(page, { data = profile, delayed = false, profileStatus = 200 } = {}) {
  const calls = { purchases: [], profileWrites: 0, tripReads: 0, profileReads: 0, failRefresh: false, occupied: [] };
  let release;
  const gate = delayed ? new Promise(resolve => { release = resolve; }) : Promise.resolve();
  await page.addInitScript(() => {
    sessionStorage.setItem("travela_token", "autofill-test-token");
    sessionStorage.setItem("travela_user", JSON.stringify({ role: "Passenger", fullName: "Hesap Sahibi" }));
  });
  await page.route("**/api/**", async route => {
    const path = new URL(route.request().url()).pathname;
    if (path === "/api/profile") {
      if (route.request().method() !== "GET") calls.profileWrites++;
      calls.profileReads++;
      expect(route.request().headers().authorization).toBe("Bearer autofill-test-token");
      await gate;
      return route.fulfill({ status: profileStatus, json: data });
    }
    if (path === "/api/trips/public/12") {
      calls.tripReads++;
      if (calls.failRefresh) return route.fulfill({ status: 503, json: {} });
      return route.fulfill({ json: { id: 12, companyName: "Test Firma", departureCity: "İzmir", arrivalCity: "Ankara",
        departureTime: "2099-12-17T07:00:00Z", arrivalTime: "2099-12-17T13:00:00Z", durationMinutes: 360,
        price: 300, availableSeats: 9, seatCapacity: 9, status: "Scheduled", canReserve: true, seatPlanAvailable: true,
        seatLayout: "2+1", seats: buildSeats("2+1", 9), occupiedSeats: calls.occupied, seatOccupancies: [] } });
    }
    if (path === "/api/purchases/options") return route.fulfill({ json: { provider: "LocalTest", requiresBuyer: false } });
    if (path === "/api/campaigns/quote") {
      const body = route.request().postDataJSON();
      expect(body).not.toHaveProperty("identityNumber");
      expect(body).not.toHaveProperty("passengers");
      return route.fulfill({ json: { amount: 300 * body.passengerCount, originalAmount: 300 * body.passengerCount, discountAmount: 0 } });
    }
    if (path === "/api/purchases") {
      calls.purchases.push(route.request().postDataJSON());
      return route.fulfill({ json: { id } });
    }
    if (path === `/api/purchases/${id}`) return route.fulfill({ json: { id, amount: 300, provider: "LocalTest", isTest: true,
      paymentStatus: "Pending", checkoutState: "Ready", legalAccepted: true, tickets: [], holdExpiresAt: "2099-12-17T06:59:00Z" } });
    return route.fulfill({ json: [] });
  });
  return { calls, release: () => release?.() };
}

for (const width of [390, 1440]) test(`İlk koltuk otomatik dolar; form düzenlenir, temizlenir ve onaylanan bilgiler gönderilir: ${width}px`, async ({ page }) => {
  await page.setViewportSize({ width, height: 1000 });
  const { calls } = await setup(page); await installSeatSelectionMock(page); await page.goto("/trips/12");
  // Gender remains an explicit choice even when the complete account profile is loaded.
  await page.getByRole("button", { name: /^Koltuk 8,/ }).click();
  await expect(name(page, 8)).toHaveCount(0);
  await page.getByRole("dialog", { name: "Koltuk 8", exact: true }).getByRole("button", { name: "Erkek", exact: true }).click();
  await expect(name(page, 8)).toHaveValue(profile.fullName);
  await expect(identity(page, 8)).toHaveValue(profile.identityNumber);
  await expect(page.getByLabel("İletişim e-posta")).toHaveValue(profile.email);
  await expect(page.getByLabel("İletişim telefon")).toHaveValue(profile.phoneNumber);
  await clear(page, 8);
  await expect(name(page, 8)).toHaveValue(""); await expect(identity(page, 8)).toHaveValue("");
  await expect(page.getByLabel("İletişim e-posta")).toHaveValue(profile.email);
  await name(page, 8).fill("Başka Yolcu"); await identity(page, 8).fill("12345678950");
  await page.getByLabel("İletişim e-posta").fill("other@example.test");
  await page.getByLabel("İletişim telefon").fill("+905551112233");
  await page.getByLabel("Kupon kodunuz var mı?").fill("TEST");
  await page.getByRole("button", { name: "Uygula", exact: true }).click();
  await expect(page.locator(".campaign-price")).toBeVisible();
  await expect(name(page, 8)).toHaveValue("Başka Yolcu");
  await expect(identity(page, 8)).toHaveValue("12345678950");
  await page.locator(".passenger-checkout-form").screenshot({ path: `../tmp/passenger-autofill/form-${width}.png` });
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width + 1);
  expect(await page.evaluate(() => [...Object.values(localStorage), ...Object.values(sessionStorage)].some(value => value.includes("12345678950") || value.includes("10000000146")))).toBe(false);
  await page.getByRole("checkbox", { name: /Yolcu bilgilerini kontrol ettim/ }).check();
  await page.getByRole("checkbox", { name: /Mesafeli satış sözleşmesini ve ön bilgilendirme/ }).check();
  await page.getByRole("button", { name: "Ödemeye devam et", exact: true }).click();
  await expect(page).toHaveURL(`/purchase/${id}`);
  expect(calls.purchases[0]).toMatchObject({ contactEmail: "other@example.test", contactPhone: "+905551112233",
    passengers: [{ seatNumber: 8, fullName: "Başka Yolcu", identityNumber: "12345678950", gender: "Male" }] });
  expect(calls.profileWrites).toBe(0);
});

test("Birden çok koltuk ve kaldırma, yolcu bilgisini başka koltuğa taşımaz; açık aktarım kullanılabilir", async ({ page }) => {
  await setup(page); await installSeatSelectionMock(page); await page.goto("/trips/12");
  await chooseSeat(page, 8); await expect(name(page, 8)).toHaveValue(profile.fullName);
  await chooseSeat(page, 2); await expect(name(page, 2)).toHaveValue(""); await expect(identity(page, 2)).toHaveValue("");
  await name(page, 2).fill("İkinci Yolcu"); await identity(page, 2).fill("12345678950");
  await chooseSeat(page, 5); await expect(name(page, 5)).toHaveValue("");
  await expect(page.getByRole("group", { name: "İletişim Bilgileri", exact: true })).toHaveCount(1);
  await page.getByRole("button", { name: "Koltuğu çıkar · 5", exact: true }).click();
  await expect(name(page, 8)).toHaveValue(profile.fullName); await expect(name(page, 2)).toHaveValue("İkinci Yolcu");
  await page.getByRole("button", { name: "Koltuğu çıkar · 8", exact: true }).click();
  await expect(name(page, 2)).toHaveValue("İkinci Yolcu"); await expect(identity(page, 2)).toHaveValue("12345678950");
  await chooseSeat(page, 8); await expect(name(page, 8)).toHaveValue("");
  await useProfile(page, 8); await expect(name(page, 8)).toHaveValue(profile.fullName);
  for (const seat of [8, 2]) await page.getByRole("button", { name: `Koltuğu çıkar · ${seat}`, exact: true }).click();
  await chooseSeat(page, 5); await expect(name(page, 5)).toHaveValue("");
  await expect(page.getByLabel("İletişim telefon")).toHaveValue(profile.phoneNumber);
});

test("Geç profil yalnız dokunulmamış alanları doldurur; bilerek boş bırakılan alanı değiştirmez", async ({ page }) => {
  const { release } = await setup(page, { delayed: true }); await installSeatSelectionMock(page); await page.goto("/trips/12");
  await chooseSeat(page, 8); await chooseSeat(page, 2);
  await name(page, 8).fill("Elle Girilen Yolcu");
  await page.getByLabel("İletişim e-posta").fill("sil@example.test"); await page.getByLabel("İletişim e-posta").fill("");
  release();
  await expect(identity(page, 8)).toHaveValue(profile.identityNumber);
  await expect(name(page, 8)).toHaveValue("Elle Girilen Yolcu");
  await expect(page.getByLabel("İletişim e-posta")).toHaveValue("");
  await expect(page.getByLabel("İletişim telefon")).toHaveValue(profile.phoneNumber);
  await expect(name(page, 2)).toHaveValue(""); await expect(identity(page, 2)).toHaveValue("");
});

test("Profil gelmeden temizlenen veya kaldırılan ilk koltuk otomatik doldurulmaz", async ({ page }) => {
  const { release } = await setup(page, { delayed: true }); await installSeatSelectionMock(page); await page.goto("/trips/12");
  await chooseSeat(page, 8); await clear(page, 8); release();
  await expect(page.getByLabel("İletişim e-posta")).toHaveValue(profile.email);
  await expect(name(page, 8)).toHaveValue(""); await expect(identity(page, 8)).toHaveValue("");
});

test("İlk koltuk profil gelmeden kaldırılırsa başka koltuğa aktarılmaz", async ({ page }) => {
  const { release } = await setup(page, { delayed: true }); await installSeatSelectionMock(page); await page.goto("/trips/12");
  await chooseSeat(page, 8); await chooseSeat(page, 2);
  await page.getByRole("button", { name: "Koltuğu çıkar · 8", exact: true }).click(); release();
  await expect(page.getByLabel("İletişim e-posta")).toHaveValue(profile.email);
  await expect(name(page, 2)).toHaveValue(""); await expect(identity(page, 2)).toHaveValue("");
  await useProfile(page, 2); await expect(identity(page, 2)).toHaveValue(profile.identityNumber);
});

for (const mode of ["missing", "failed"]) test(`Eksik veya alınamayan profil koltuk seçimini engellemez: ${mode}`, async ({ page }) => {
  const { calls } = await setup(page, { data: mode === "missing" ? { fullName: "Eksik Profil" } : {}, profileStatus: mode === "failed" ? 503 : 200 });
  await installSeatSelectionMock(page); await page.goto("/trips/12"); await chooseSeat(page, 8);
  if (mode === "failed") await expect(page.getByText("Hesap bilgileri alınamadı. Bilgileri elle doldurun.")).toBeVisible();
  else await expect(name(page, 8)).toHaveValue("Eksik Profil");
  await expect(identity(page, 8)).toHaveValue(""); await expect(page.getByLabel("İletişim telefon")).toHaveValue("");
  await page.getByRole("checkbox", { name: /Yolcu bilgilerini kontrol ettim/ }).check();
  await page.getByRole("checkbox", { name: /Mesafeli satış sözleşmesini ve ön bilgilendirme/ }).check();
  await page.getByRole("button", { name: "Ödemeye devam et", exact: true }).click(); expect(calls.purchases).toHaveLength(0);
  await name(page, 8).fill("Elle Tamamlanan Yolcu"); await identity(page, 8).fill("12345678950");
  await page.getByLabel("İletişim e-posta").fill("manual@example.test"); await page.getByLabel("İletişim telefon").fill("+905551112233");
  await page.getByRole("button", { name: "Ödemeye devam et", exact: true }).click();
  await expect(page).toHaveURL(`/purchase/${id}`); expect(calls.profileWrites).toBe(0);
});

test("Plan yenileme ve ağ hatası formu korur; kişinin kendi sunucu tutması çakışma sayılmaz", async ({ page }) => {
  const { calls } = await setup(page); await installSeatSelectionMock(page); await page.goto("/trips/12"); await chooseSeat(page, 8);
  await name(page, 8).fill("Düzenlenen Yolcu"); await identity(page, 8).fill("12345678950");
  await page.getByLabel("İletişim e-posta").fill("edited@example.test");
  const refresh = page.getByRole("button", { name: "Koltuk planını yenile", exact: true });
  await refresh.click(); await expect(refresh).toBeEnabled();
  await expect(name(page, 8)).toHaveValue("Düzenlenen Yolcu"); await expect(identity(page, 8)).toHaveValue("12345678950");
  calls.failRefresh = true; await refresh.click();
  await expect(page.getByText("Sefer bilgileri şu anda alınamıyor.")).toBeVisible();
  await expect(name(page, 8)).toHaveValue("Düzenlenen Yolcu");
  await expect(page.getByRole("button", { name: "Ödemeye devam et", exact: true })).toBeDisabled();
  calls.failRefresh = false; calls.occupied = [8]; await refresh.click(); await expect(refresh).toBeEnabled();
  await expect(page.getByRole("button", { name: /^Koltuk 8,/ })).toHaveAttribute("aria-pressed", "true");
  await expect(page.getByText("Koltuk başka biri tarafından alınmış veya geçici olarak rezerve edilmiş. Koltuğu çıkarınız.")).toHaveCount(0);
  await expect(name(page, 8)).toHaveValue("Düzenlenen Yolcu");
  await expect(page.getByLabel("İletişim e-posta")).toHaveValue("edited@example.test");
  expect(calls.profileReads).toBeLessThanOrEqual(2); expect(calls.profileWrites).toBe(0);
});
