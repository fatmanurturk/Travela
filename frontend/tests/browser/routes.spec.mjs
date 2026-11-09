import { test, expect } from "@playwright/test";

const first = { id: 1, departureCity: "İstanbul", arrivalCity: "Ankara", distanceKm: 450, estimatedDurationMinutes: 375,
  isActive: true, tripCount: 2, plannedTripCount: 1, hasTrips: true };
const second = { ...first, id: 2, departureCity: "Ankara", arrivalCity: "İzmir", isActive: false, tripCount: 0, plannedTripCount: 0, hasTrips: false };
async function setup(page, role = "Company", records = [first, second]) {
  const data = structuredClone(records);
  await page.addInitScript(role => {
    sessionStorage.setItem("travela_token", "route-ui-test");
    sessionStorage.setItem("travela_user", JSON.stringify({ role, name: "Test Kullanıcı" }));
  }, role);
  await page.route("**/api/company/profile", r => r.fulfill({ json: { name: "Test Firma" } }));
  await page.route("**/api/routes/cities", r => r.fulfill({ json: ["İstanbul", "Ankara", "İzmir", "Bursa"] }));
  await page.route("**/api/routes?includeInactive=true", r => r.fulfill({ json: data }));
  await page.route(/\/api\/routes\/\d+\?pageNumber=/, r => r.fulfill({ json: {
    route: data.find(route => route.id === Number(new URL(r.request().url()).pathname.split("/").at(-1))), pageNumber: 1, totalPages: 1,
    trips: [{ id: 8, departureTime: "2099-01-01T07:00:00Z", arrivalTime: "2099-01-01T13:00:00Z", busPlateNumber: "34 TEST 01", price: 500, status: "Scheduled" }],
  } }));
  return data;
}

for (const width of [390, 1440]) test(`Firma güzergâh listesi, filtreler, detay ve duyarlı tasarım: ${width}px`, async ({ page }) => {
  await page.setViewportSize({ width, height: 1000 }); await setup(page);
  await page.goto("/company/routes");
  await expect(page.getByRole("link", { name: "Güzergâh Yönetimi", exact: true })).toHaveClass(/active/);
  const labels = await page.locator(".company-menu-link").allTextContents();
  expect(labels.slice(0, 4)).toEqual(["Genel Bakış", "Otobüs Yönetimi", "Güzergâh Yönetimi", "Sefer Yönetimi"]);
  await expect(page.getByRole("button", { name: /Yeni Güzergâh|Düzenle|Pasife Al|Aktif Yap/ })).toHaveCount(0);
  await expect(page.locator(".route-stats .stat-card h2")).toHaveText(["2", "1", "1", "2"]);
  await page.getByLabel("Durum filtresi").selectOption("false");
  await expect(page.locator(".route-table tbody tr")).toHaveCount(1);
  await expect(page.locator(".route-table")).toContainText("İzmir");
  await page.getByRole("button", { name: "Filtreleri Temizle" }).click();
  await page.getByLabel("Genel arama").fill("istanbul");
  await expect(page.locator(".route-table tbody tr")).toHaveCount(1);
  await expect(page.locator(".route-table")).toContainText("6 saat 15 dk");
  await page.getByRole("button", { name: "Detay", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Firmanızın bu güzergâhtaki seferleri" })).toBeVisible();
  await expect(page.locator(".route-detail")).toContainText("34 TEST 01");
  const size = await page.evaluate(() => ({ viewport: document.documentElement.clientWidth, width: document.documentElement.scrollWidth }));
  expect(size.width).toBeLessThanOrEqual(size.viewport + 1);
  await page.screenshot({ path: `../tmp/route-qa/company-${width}.png`, fullPage: true });
});

test("Admin formu süreyi dakikaya çevirir; kayıt listede ve kartlarda görünür", async ({ page }) => {
  const data = await setup(page, "Admin"); let sent;
  await page.route(/\/api\/routes$/, async r => {
    sent = r.request().postDataJSON(); data.push({ ...sent, id: 3, tripCount: 0, plannedTripCount: 0, hasTrips: false });
    await r.fulfill({ status: 201, json: { message: "Güzergâh oluşturuldu." } });
  });
  await page.goto("/admin/routes"); await page.getByRole("button", { name: "+ Yeni Güzergâh Ekle", exact: true }).click();
  await page.getByLabel("Kalkış Şehri *", { exact: true }).fill("Bursa");
  await page.getByLabel("Varış Şehri *", { exact: true }).fill("İzmir");
  await page.getByLabel("Mesafe (km)", { exact: true }).fill("330");
  await page.getByLabel("Tahmini Süre (saat)", { exact: true }).fill("4");
  await page.getByLabel("Tahmini Süre (dakika)", { exact: true }).fill("30");
  await page.getByRole("button", { name: "Kaydet", exact: true }).click();
  await expect(page.locator(".form-alert.success")).toHaveText("Güzergâh oluşturuldu.");
  expect(sent).toMatchObject({ departureCity: "Bursa", arrivalCity: "İzmir", distanceKm: 330, estimatedDurationMinutes: 270, isActive: true });
  expect(sent).not.toHaveProperty("companyId");
  await expect(page.getByRole("heading", { name: "Yeni Güzergâh Oluştur" })).toHaveCount(0);
  await expect(page.locator(".route-stats .stat-card h2")).toHaveText(["3", "2", "1", "2"]);
  await expect(page.locator(".route-table")).toContainText("Bursa");
  await page.screenshot({ path: "../tmp/route-qa/admin-desktop.png", fullPage: true });
  await page.setViewportSize({ width: 390, height: 1000 });
  await page.getByRole("button", { name: "+ Yeni Güzergâh Ekle", exact: true }).click();
  await page.screenshot({ path: "../tmp/route-qa/admin-mobile-form.png", fullPage: true });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth + 1)).toBe(true);
});

test("Bağlı güzergâhın şehirleri kilitli; API hatasında form korunur", async ({ page }) => {
  await setup(page, "Admin");
  await page.route("**/api/routes/1", r => r.fulfill({ status: 409, json: { message: "Güzergâh eşzamanlı değiştirildi. Tekrar deneyin." } }));
  await page.goto("/admin/routes");
  await page.locator(".route-table tbody tr").filter({ hasText: "İstanbul" }).getByRole("button", { name: "Düzenle" }).click();
  await expect(page.getByLabel("Kalkış Şehri *", { exact: true })).toBeDisabled();
  await expect(page.getByLabel("Varış Şehri *", { exact: true })).toBeDisabled();
  await page.getByLabel("Mesafe (km)", { exact: true }).fill("455");
  await page.getByRole("button", { name: "Kaydet", exact: true }).click();
  await expect(page.getByRole("alert")).toContainText("Tekrar deneyin");
  await expect(page.getByLabel("Mesafe (km)", { exact: true })).toHaveValue("455");
});

test("Pasife alma planlanmış sefer uyarısını onaylatır; tekrar aktif yapılabilir", async ({ page }) => {
  const data = await setup(page, "Admin"); const sent = [];
  await page.route("**/api/routes/1/status", async r => {
    const body = r.request().postDataJSON(); sent.push(body);
    if (!body.isActive && !body.confirmDeactivation) return r.fulfill({ status: 409, json: {
      code: "route_deactivation_confirmation_required", message: "1 planlanmış seferin yeni bilet satışı kapanır. Devam edilsin mi?",
    } });
    data[0].isActive = body.isActive;
    return r.fulfill({ json: { message: body.isActive ? "Güzergâh aktif hale getirildi." : "Güzergâh pasife alındı." } });
  });
  await page.goto("/admin/routes");
  page.once("dialog", async dialog => { expect(dialog.message()).toContain("bilet satışı"); await dialog.dismiss(); });
  await page.getByRole("button", { name: "Pasife Al", exact: true }).click();
  await expect(page.getByRole("button", { name: "Pasife Al", exact: true })).toBeEnabled();
  expect(sent).toHaveLength(1);
  page.once("dialog", dialog => dialog.accept());
  await page.getByRole("button", { name: "Pasife Al", exact: true }).click();
  await expect(page.locator(".form-alert.success")).toHaveText("Güzergâh pasife alındı.");
  expect(sent.at(-1)).toEqual({ isActive: false, confirmDeactivation: true });
  await page.locator(".route-table tbody tr").filter({ hasText: "İstanbul" }).getByRole("button", { name: "Aktif Yap" }).click();
  await expect(page.locator(".form-alert.success")).toHaveText("Güzergâh aktif hale getirildi.");
});

test("Sayfalama, sıralama, boş sonuç ve API hatasından sonra yeniden yükleme", async ({ page }) => {
  await setup(page, "Company", Array.from({ length: 12 }, (_, i) => ({ ...first, id: i + 1 })));
  await page.goto("/company/routes");
  await expect(page.locator(".route-table tbody tr")).toHaveCount(10);
  await page.getByRole("button", { name: "Sonraki", exact: true }).click();
  await expect(page.locator(".route-table tbody tr")).toHaveCount(2);
  await page.getByRole("button", { name: /Güzergâh ID/ }).click();
  await expect(page.locator(".route-table tbody tr").first()).toContainText("#1");
  await page.getByLabel("Genel arama").fill("bulunmayan");
  await expect(page.getByText("Filtrelerle eşleşen güzergâh bulunamadı.")).toBeVisible();
  await page.route("**/api/routes?includeInactive=true", r => r.fulfill({ status: 500, json: { message: "Güzergâh verileri alınamadı." } }));
  await page.reload(); await expect(page.getByRole("alert")).toContainText("Güzergâh verileri alınamadı.");
  await page.route("**/api/routes?includeInactive=true", r => r.fulfill({ json: [] }));
  await page.getByRole("button", { name: "Yenile", exact: true }).click();
  await expect(page.getByText(/Henüz güzergâh bulunmuyor/)).toBeVisible();
});

test("Sefer oluşturma yalnızca aktif güzergâhı şehir ve mesafeyle sunar", async ({ page }) => {
  await setup(page);
  await page.route("**/api/routes", r => r.fulfill({ json: [first, second] }));
  await page.route("**/api/buses?*", r => r.fulfill({ json: { items: [] } }));
  await page.route("**/api/terminals", r => r.fulfill({ json: [] }));
  await page.route("**/api/trips?*", r => r.fulfill({ json: { items: [], totalCount: 0, totalPages: 1 } }));
  await page.route("**/api/trips/summary", r => r.fulfill({ json: { totalTrips: 0 } }));
  await page.goto("/company/trips"); await page.getByRole("button", { name: "+ Yeni Sefer Ekle", exact: true }).click();
  const options = page.getByRole("combobox", { name: "Güzergâh *", exact: true }).locator("option");
  await expect(options).toHaveText(["Güzergâh seçin", "İstanbul – Ankara · 450 km"]);
  await page.route("**/api/routes", r => r.fulfill({ json: [] }));
  await page.reload(); await page.getByRole("button", { name: "+ Yeni Sefer Ekle", exact: true }).click();
  await expect(page.getByText(/kullanılabilir aktif güzergâh bulunmuyor/)).toBeVisible();
});

test("ID 9 detay isteği GET ile doğru URL'ye gider; yenilemede toplam korunur", async ({ page }) => {
  await setup(page, "Company", [{ ...first, id: 9 }]); let detailRequest;
  await page.route("**/api/routes/9?pageNumber=1", r => {
    detailRequest = r.request(); return r.fulfill({ json: { route: { ...first, id: 9 }, pageNumber: 1, totalPages: 1,
      trips: [{ id: 90, departureTime: "2099-01-01T07:00:00Z", arrivalTime: "2099-01-01T13:00:00Z", busPlateNumber: "34 TEST 01", price: 500, status: "Scheduled" }] } });
  });
  await page.goto("/company/routes"); await page.getByRole("button", { name: "Detay", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Güzergâh #9 Detayları" })).toBeVisible();
  expect(detailRequest.method()).toBe("GET"); expect(new URL(detailRequest.url()).pathname).toBe("/api/routes/9");
  expect(detailRequest.headers().authorization).toBe("Bearer route-ui-test");
  await expect(page.locator(".route-detail th")).toHaveText(["Sefer ID", "Kalkış Tarihi", "Varış Tarihi", "Otobüs", "Fiyat", "Sefer Durumu"]);
  await expect(page.locator(".route-detail")).toContainText("16:00:00");
  await page.reload(); await expect(page.locator(".route-stats .stat-card h2").last()).toHaveText("2");
});

for (const value of [null, "geçersiz", "missing"]) test(`Geçersiz tripCount (${value}) NaN veya 0 ile gizlenmez`, async ({ page }) => {
  const record = { ...first, tripCount: value === "missing" ? undefined : value };
  await setup(page, "Company", [record]); await page.goto("/company/routes");
  await expect(page.getByRole("alert")).toContainText("tripCount");
  await expect(page.locator(".route-stats .stat-card h2")).toHaveText(["—", "—", "—", "—"]);
  await expect(page.locator(".route-table")).toHaveCount(0);
});

test("Gerçek sıfır 0 görünür; string sefer sayısı sayısal toplam verir", async ({ page }) => {
  const records = await setup(page, "Company", [{ ...first, tripCount: 0, plannedTripCount: 0 }]);
  await page.goto("/company/routes"); await expect(page.locator(".route-stats .stat-card h2").last()).toHaveText("0");
  records[0].tripCount = "3";
  await page.getByRole("button", { name: "Yenile", exact: true }).click();
  await expect(page.locator(".route-stats .stat-card h2").last()).toHaveText("3");
  await expect(page.locator(".route-table tbody td").nth(5)).toHaveText("3");
});

for (const [status, json, message] of [[401, {}, "Oturumunuz"], [403, {}, "yetkiniz"], [404, {}, "Backend"],
  [404, { code: "route_not_found", message: "Güzergâh bulunamadı." }, "Listeyi yenileyip"]])
test(`Detay HTTP ${status}: ${message} mesajı ayrı gösterilir`, async ({ page }) => {
  await setup(page);
  await page.route("**/api/routes/1?pageNumber=1", r => r.fulfill({ status, json }));
  await page.goto("/company/routes"); await page.locator(".route-table tbody tr").filter({ hasText: "İstanbul" }).getByRole("button", { name: "Detay" }).click();
  await expect(page.getByRole("alert")).toContainText(message);
  await expect(page.locator(".route-detail")).toHaveCount(0);
  if (status === 401) await expect(page.getByRole("link", { name: "Giriş yap", exact: true })).toBeVisible();
});
