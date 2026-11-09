import { test, expect } from "@playwright/test";

const footerLinks = [
  ["Hakkımızda", "/hakkimizda"],
  ["İletişim", "/iletisim"],
  ["Yardım", "/yardim"],
  ["Gizlilik ve KVKK Aydınlatma Metni", "/gizlilik-ve-kvkk"],
  ["Mesafeli Satış Sözleşmesi", "/mesafeli-satis-sozlesmesi"],
  ["İade ve İptal Koşulları", "/iade-ve-iptal"],
  ["Çerez Politikası", "/cerez-politikasi"],
];

async function mockPublicApi(page) {
  await page.route("**/api/**", route => {
    const path = new URL(route.request().url()).pathname;
    const arrays = [
      "/api/routes/public",
      "/api/trips/upcoming",
      "/api/trips/featured-routes",
      "/api/campaigns/public",
      "/api/tickets/mine",
      "/api/reservations/mine",
    ];
    if (arrays.includes(path)) return route.fulfill({ json: [] });
    if (path === "/api/trips/search") {
      return route.fulfill({
        json: { items: [], companies: [], totalCount: 0, totalPages: 0 },
      });
    }
    return route.fulfill({
      status: 404,
      json: { message: "Kayıt bulunamadı." },
    });
  });
}

for (const path of ["/olmayan-sayfa", "/admin/olmayan-sayfa", "/company/olmayan-sayfa"]) {
  test(`Bilinmeyen adres 404 ekranını gösterir: ${path}`, async ({ page }) => {
    await page.goto(path);
    await expect(page.getByRole("heading", { name: "Sayfa bulunamadı" })).toBeVisible();
    await expect(page.getByRole("banner")).toHaveCount(1);
    await expect(page.getByRole("contentinfo")).toHaveCount(1);
    const main = page.getByRole("main");
    await expect(main.getByRole("link", { name: "Ana sayfaya dön" })).toHaveAttribute("href", "/");
    await expect(main.getByRole("link", { name: "Sefer ara", exact: true })).toHaveAttribute("href", "/#sefer-ara");
    await expect(page).toHaveURL(new RegExp(path + "$"));
  });
}

for (const [role, path, panel] of [
  ["Admin", "/admin/olmayan-sayfa", "Yönetici Paneli"],
  ["Company", "/company/olmayan-sayfa", "Firma Paneli"],
]) {
  test(`${role} oturumunda bilinmeyen panel adresinden geri dönülebilir`, async ({ page }) => {
    await page.addInitScript(role => {
      sessionStorage.setItem("travela_token", "isolated-ui-session");
      sessionStorage.setItem("travela_user", JSON.stringify({ role }));
    }, role);
    await page.goto(path);
    await expect(page.getByRole("heading", { name: "Sayfa bulunamadı" })).toBeVisible();
    await expect(page.getByRole("banner").getByRole("button", { name: panel })).toBeVisible();
    await expect(page.locator(".admin-layout, .company-layout")).toHaveCount(0);
  });
}

test("Mevcut panel adreslerinin giriş kontrolü korunur", async ({ page }) => {
  for (const path of ["/admin/buses", "/company/trips"]) {
    await page.goto(path);
    await expect(page).toHaveURL(/\/login\?returnTo=/);
    await expect(page.getByRole("heading", { name: "Sayfa bulunamadı" })).toHaveCount(0);
  }
});

for (const path of [
  "/",
  "/search?all=true",
  "/trips/999999",
  "/campaigns",
  "/login",
  "/register",
  "/forgot-password",
  "/resend-activation",
  "/reset-password",
  "/activate-account",
]) {
  test(`Genel sayfada tek ortak footer ve tüm bağlantılar bulunur: ${path}`, async ({ page }) => {
    await mockPublicApi(page);
    await page.goto(path);
    const footer = page.getByRole("contentinfo");
    await expect(footer).toHaveCount(1);
    await expect(footer.getByRole("navigation", { name: "Alt menü" })).toHaveCount(1);
    for (const [name, href] of footerLinks) {
      await expect(footer.getByRole("link", { name, exact: true })).toHaveAttribute("href", href);
    }
  });
}

test("Yolcu ekranı da ortak footer kullanır", async ({ page }) => {
  await mockPublicApi(page);
  await page.addInitScript(() => {
    sessionStorage.setItem("travela_token", "isolated-ui-session");
    sessionStorage.setItem("travela_user", JSON.stringify({ role: "Passenger" }));
  });
  await page.goto("/tickets");
  await expect(page.getByRole("heading", { name: "Biletlerim", exact: true })).toBeVisible();
  await expect(page.getByRole("contentinfo")).toHaveCount(1);
});

test("404 ekranındaki sefer arama bağlantısı ana sayfadaki forma gider", async ({ page }) => {
  await mockPublicApi(page);
  await page.goto("/olmayan-sayfa");
  await page.getByRole("main").getByRole("link", { name: "Sefer ara", exact: true }).click();
  await expect(page).toHaveURL(/\/#sefer-ara$/);
  await expect(page.locator("#sefer-ara")).toBeInViewport();
});

for (const width of [390, 1440]) {
  test(`404 ve footer duyarlı tasarımı taşmaz: ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 1000 });
    await page.goto("/olmayan-sayfa");
    await expect(page.getByRole("heading", { name: "Sayfa bulunamadı" })).toBeVisible();
    const size = await page.evaluate(() => ({
      width: document.documentElement.scrollWidth,
      viewport: document.documentElement.clientWidth,
    }));
    expect(size.width).toBeLessThanOrEqual(size.viewport + 1);
    await page.screenshot({
      path: `../tmp/site-shell/not-found-${width}.png`,
      fullPage: true,
    });
  });
}
