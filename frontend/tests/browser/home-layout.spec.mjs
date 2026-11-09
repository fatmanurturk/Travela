import { test, expect } from "@playwright/test";

// Layout fixtures stay inside intercepted browser requests; the application DB is never written.
const trip = { id: 42, companyName: "Test Turizm", departureCity: "Diyarbakır", arrivalCity: "İstanbul",
  departureTime: "2099-01-02T07:30:00Z", arrivalTime: "2099-01-02T21:30:00Z", durationMinutes: 840, price: 1250, availableSeats: 17 };
const route = { routeId: 9, departureCity: "Diyarbakır", arrivalCity: "İstanbul", upcomingTripCount: 1, startingPrice: 1250 };
async function setup(page, count) {
  await page.emulateMedia({ reducedMotion: "reduce" });
  await page.route("**/api/routes/public", r => r.fulfill({ json: [route] }));
  await page.route("**/api/trips/upcoming?count=6", r => r.fulfill({ json: Array.from({ length: count }, (_, i) => ({ ...trip, id: trip.id + i })) }));
  await page.route("**/api/trips/featured-routes?count=6", r => r.fulfill({ json: Array.from({ length: count }, (_, i) => ({ ...route, routeId: route.routeId + i })) }));
}

for (const width of [390, 768, 1440]) for (const count of [1, 2, 6]) {
  test(`${count} kart, ${width}px: içerik alanı dengeli dolar ve taşma oluşmaz`, async ({ page }) => {
    await page.setViewportSize({ width, height: 1000 }); await setup(page, count); await page.goto("/");
    await expect(page.locator(".upcoming-card")).toHaveCount(count);
    await expect(page.locator(".route-card")).toHaveCount(count);
    const columns = width <= 600 ? 1 : count === 1 ? 1 : width < 1000 ? 2 : Math.min(3, count);
    for (const gridName of [".upcoming-grid", ".route-grid"]) {
      const metrics = await page.locator(gridName).evaluate(grid => {
        const style = getComputedStyle(grid), card = grid.firstElementChild.getBoundingClientRect();
        return { columns: style.gridTemplateColumns.split(" ").filter(track => Number.parseFloat(track) > 0).length, width: grid.getBoundingClientRect().width, cardWidth: card.width };
      });
      expect(metrics.columns).toBe(columns);
      expect(metrics.cardWidth / metrics.width).toBeGreaterThan(1 / columns - 0.04);
      if (count === 1) expect(metrics.cardWidth).toBeCloseTo(metrics.width, 0);
    }
    await expect(page.locator(".upcoming-card").first()).toContainText("Test Turizm");
    await expect(page.locator(".upcoming-card").first()).toContainText("17 boş koltuk");
    await expect(page.locator(".upcoming-card").first()).toContainText("10:30");
    await expect(page.locator(".upcoming-card").first()).toContainText("1.250,00");
    await expect(page.locator(".route-card").first()).toContainText("1 yaklaşan sefer");
    const sizes = await page.evaluate(() => ({ viewport: document.documentElement.clientWidth, document: document.documentElement.scrollWidth }));
    expect(sizes.document).toBeLessThanOrEqual(sizes.viewport + 1);
    const sections = await page.locator("#yaklasan-seferler, .route-section").evaluateAll(sections => sections.map(section => ({
      padding: Number.parseFloat(getComputedStyle(section).paddingTop), height: section.getBoundingClientRect().height,
    })));
    expect(sections.every(section => section.padding <= 48)).toBe(true);
    if (count === 1) {
      await page.locator("#yaklasan-seferler").screenshot({ path: `../tmp/home-layout/upcoming-${width}.png` });
      await page.locator(".route-section").screenshot({ path: `../tmp/home-layout/featured-${width}.png` });
      if (width === 1440) {
        const main = await page.locator(".upcoming-card-main").boundingBox();
        const schedule = await page.locator(".upcoming-card-schedule").boundingBox();
        const action = await page.locator(".upcoming-card-bottom").boundingBox();
        expect(main.x).toBeLessThan(schedule.x); expect(schedule.x).toBeLessThan(action.x);
      }
    }
  });
}

test("Boş veride kart üretilmez; açıklama ve arama bağlantısı gösterilir", async ({ page }) => {
  await setup(page, 0); await page.goto("/");
  await expect(page.getByRole("heading", { name: "Yaklaşan sefer bulunmuyor" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Şu anda öne çıkan güzergâh bulunmuyor" })).toBeVisible();
  await expect(page.locator(".upcoming-card, .route-card")).toHaveCount(0);
  await expect(page.locator(".home-empty-state").getByRole("link", { name: "Sefer ara", exact: true })).toHaveAttribute("href", "#sefer-ara");
});

test("Detay ve güzergâh bağlantıları gerçek API kimliğini ve arama kriterlerini korur", async ({ page }) => {
  await setup(page, 1); await page.goto("/");
  await expect(page.getByRole("link", { name: "Seferi İncele", exact: true })).toHaveAttribute("href", "/trips/42");
  const href = await page.locator(".route-card").getAttribute("href");
  const url = new URL(href, "http://localhost");
  expect(url.pathname).toBe("/search");
  expect(Object.fromEntries(url.searchParams)).toEqual({ from: "Diyarbakır", to: "İstanbul", date: "2099-01-02", passengers: "1", tripType: "one-way" });
  await page.route("**/api/trips/search?*", r => r.fulfill({ json: { items: [], companies: [], totalCount: 0, totalPages: 0 } }));
  await page.locator(".route-card").click(); await expect(page).toHaveURL(/\/search\?/);
  await expect(page.locator(".results-message.error")).toHaveCount(0);
});

test("API hatası boş sefer veya fiyat olarak gizlenmez", async ({ page }) => {
  await setup(page, 1);
  await page.route("**/api/trips/upcoming?count=6", r => r.fulfill({ status: 500, json: {} }));
  await page.goto("/");
  await expect(page.getByRole("alert")).toContainText("Yaklaşan seferler yüklenemedi");
  await expect(page.locator(".upcoming-card")).toHaveCount(0);
  await expect(page.locator(".route-card")).toHaveCount(1);
});
