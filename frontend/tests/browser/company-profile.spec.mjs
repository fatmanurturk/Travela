import { test, expect } from "@playwright/test";

const company = { id: 3, name: "Profil Test Firması", email: "firma@example.com", phone: "02121234567", address: "İstanbul", createdAt: "2026-09-01T09:00:00Z", isActive: true };
const user = { id: 8, fullName: "Ayşe Yılmaz", email: "ayse@example.com", role: "Company", companyId: 3, companyName: company.name };

async function setup(page, width = 1440) {
  await page.setViewportSize({ width, height: 1000 });
  await page.addInitScript(sessionUser => {
    sessionStorage.setItem("travela_token", "company-profile-token");
    sessionStorage.setItem("travela_user", JSON.stringify(sessionUser));
  }, user);
  let savedUser = { ...user };
  let savedCompany = { ...company };
  const requests = [];
  page.on("request", request => { if (new URL(request.url()).pathname.startsWith("/api/")) requests.push(request); });
  await page.route("**/api/**", route => route.fulfill({ status: 404, json: { message: "Beklenmeyen API isteği." } }));
  await page.route("**/api/company/profile", route => route.fulfill({ json: savedCompany }));
  await page.route("**/api/company/me", async route => {
    if (route.request().method() === "PUT") {
      const body = route.request().postDataJSON();
      savedCompany = { ...savedCompany, ...body, email: body.email || null };
      await route.fulfill({ json: { message: "Firma bilgileri güncellendi.", company: savedCompany } });
    } else await route.fulfill({ json: savedCompany });
  });
  await page.route("**/api/company/me/summary", route => route.fulfill({ json: { totalBuses: 4, activeBuses: 3, totalTrips: 12, activeTrips: 7 } }));
  await page.route("**/api/company/user", async route => {
    if (route.request().method() === "PUT") {
      savedUser = { ...savedUser, fullName: route.request().postDataJSON().fullName };
      await route.fulfill({ json: { message: "Ad soyad güncellendi.", fullName: savedUser.fullName } });
    } else await route.fulfill({ json: savedUser });
  });
  return { requests };
}

for (const width of [390, 1440]) test(`Firma bilgisi ve yetkili profili ayrı yüklenir: ${width}px`, async ({ page }) => {
  const { requests } = await setup(page, width);
  await page.goto("/company/profile");
  await expect(page.getByRole("heading", { name: "Yetkili Kullanıcı Bilgileri" })).toBeVisible();
  await expect(page.locator(".company-profile-page")).toContainText(user.fullName);
  await expect(page.locator(".company-profile-page")).toContainText(user.email);
  await expect(page.locator(".company-profile-page")).toContainText(company.email);
  await expect(page.locator(".company-stat-card").first().getByRole("heading")).toHaveText("4");
  expect(requests.some(request => new URL(request.url()).pathname.startsWith("/api/profile"))).toBe(false);
  const size = await page.evaluate(() => ({ viewport: document.documentElement.clientWidth, width: document.documentElement.scrollWidth }));
  expect(size.width).toBeLessThanOrEqual(size.viewport + 1);
});

test("Yetkili adı kaydı firma bilgilerini ve oturumu korur; başarı mesajı görünür kalır", async ({ page }) => {
  const { requests } = await setup(page);
  await page.goto("/company/profile");
  await page.getByRole("button", { name: "Ad Soyad Güncelle", exact: true }).click();
  await page.getByLabel("Ad Soyad", { exact: true }).fill("  Güncel Yetkili  ");
  await page.getByRole("button", { name: "Kaydet", exact: true }).click();
  await expect(page.getByRole("status")).toHaveText("Ad soyad güncellendi.");
  await expect(page.locator(".company-data-list")).toContainText(["Firma ID", "Güncel Yetkili"]);
  expect(requests.filter(request => request.method() === "PUT").map(request => new URL(request.url()).pathname)).toEqual(["/api/company/user"]);
  expect(await page.evaluate(() => JSON.parse(sessionStorage.getItem("travela_user")).fullName)).toBe("Güncel Yetkili");
  expect(await page.evaluate(() => sessionStorage.getItem("travela_token"))).toBe("company-profile-token");
  await page.reload();
  await expect(page.locator(".company-profile-page")).toContainText("Güncel Yetkili");
});

test("Firma iletişim kaydı yetkili profilinden ayrıdır; e-posta temizlenebilir", async ({ page }) => {
  const { requests } = await setup(page);
  await page.goto("/company/profile");
  await page.getByRole("button", { name: "Düzenle", exact: true }).click();
  await page.getByLabel("E-posta Adresi", { exact: true }).fill("");
  await page.getByLabel("Telefon Numarası", { exact: true }).fill("02125556677");
  await page.getByRole("button", { name: "Kaydet", exact: true }).click();
  await expect(page.getByRole("status")).toHaveText("Firma bilgileri güncellendi.");
  await expect(page.locator(".company-profile-page")).toContainText(user.email);
  expect(requests.filter(request => request.method() === "PUT").map(request => new URL(request.url()).pathname)).toEqual(["/api/company/me"]);
  await page.getByRole("button", { name: "Düzenle", exact: true }).click();
  await expect(page.getByLabel("E-posta Adresi", { exact: true })).toHaveValue("");
});

async function fillPassword(page) {
  await page.getByLabel("Mevcut Şifre", { exact: true }).fill("TestPass123!");
  await page.getByLabel("Yeni Şifre", { exact: true }).fill("CompanyPass456!");
  await page.getByLabel("Yeni Şifre Tekrar", { exact: true }).fill("CompanyPass456!");
}

test("Şifre değişikliğindeki yeni token sonraki profil işleminde kullanılır", async ({ page }) => {
  const { requests } = await setup(page);
  await page.route("**/api/company/user/change-password", route => route.fulfill({ json: { message: "Şifreniz değiştirildi ve diğer oturumlar kapatıldı.", token: "company-renewed-token" } }));
  await page.goto("/company/profile");
  await fillPassword(page);
  await page.getByRole("button", { name: "Şifre Değiştir", exact: true }).click();
  await expect(page.getByRole("status")).toContainText("Şifreniz değiştirildi");
  expect(await page.evaluate(() => sessionStorage.getItem("travela_token"))).toBe("company-renewed-token");
  await expect(page.getByLabel("Mevcut Şifre", { exact: true })).toHaveValue("");
  await page.getByRole("button", { name: "Ad Soyad Güncelle", exact: true }).click();
  await page.getByLabel("Ad Soyad", { exact: true }).fill("Yeni Yetkili");
  await page.getByRole("button", { name: "Kaydet", exact: true }).click();
  await expect(page.getByText("Ad soyad güncellendi.", { exact: true })).toBeVisible();
  expect(requests.find(request => request.method() === "PUT").headers().authorization).toBe("Bearer company-renewed-token");
});

test("Yanlış şifre ve deneme sınırı oturumu kapatmadan açıklanır", async ({ page }) => {
  await setup(page);
  let attempts = 0;
  await page.route("**/api/company/user/change-password", route => {
    attempts++;
    return route.fulfill({ status: attempts === 1 ? 400 : 429, json: { message: "Mevcut şifre yanlış." } });
  });
  await page.goto("/company/profile");
  await fillPassword(page);
  await page.getByRole("button", { name: "Şifre Değiştir", exact: true }).click();
  await expect(page.getByRole("alert")).toHaveText("Mevcut şifre yanlış.");
  await page.getByRole("button", { name: "Şifre Değiştir", exact: true }).click();
  await expect(page.getByRole("alert")).toContainText("5 dakika sonra");
  await expect(page).toHaveURL(/\/company\/profile$/);
  expect(await page.evaluate(() => sessionStorage.getItem("travela_token"))).toBe("company-profile-token");
});

test("Profil yükleme hatası yeniden denenebilir; süresi dolan oturum temizlenir", async ({ page }) => {
  await setup(page);
  let failed = true;
  await page.route("**/api/company/user", route => route.fulfill(failed ? { status: 500, json: { message: "Yetkili bilgileri alınamadı." } } : { json: user }));
  await page.goto("/company/profile");
  await expect(page.getByRole("alert")).toHaveText("Yetkili bilgileri alınamadı.");
  await expect(page.locator(".company-stat-card")).toHaveCount(0);
  failed = false;
  await page.getByRole("button", { name: "Tekrar dene", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Yetkili Kullanıcı Bilgileri" })).toBeVisible();
  await page.route("**/api/company/user/change-password", route => route.fulfill({ status: 401, json: {} }));
  await fillPassword(page);
  await page.getByRole("button", { name: "Şifre Değiştir", exact: true }).click();
  await expect(page).toHaveURL(/\/login$/);
  expect(await page.evaluate(() => sessionStorage.getItem("travela_token"))).toBeNull();
  expect(await page.evaluate(() => sessionStorage.getItem("travela_user"))).toBeNull();
});
