import { test, expect } from "@playwright/test";
import { cities } from "../helpers/cities.mjs";

const ticket = {
  id: "12345678-1234-1234-1234-123456789012", pnr: "A".repeat(32), kind: "Ticket", tripId: 12, seatNumber: 2,
  companyName: "Test Firma", passengerName: "Misafir Yolcu", passengerEmail: "g***@example.test", passengerPhone: "*******4567",
  departureCity: "İzmir", arrivalCity: "Ankara", departureTime: "2099-12-17T07:00:00Z", arrivalTime: "2099-12-17T13:00:00Z",
  cancelDeadline: "2099-12-17T05:00:00Z", amount: 300, currency: "TRY", isTest: true,
  paymentStatus: "Paid", ticketStatus: "Active", refundStatus: "None", refundAmount: 0, canCancel: false,
};
const menu = page => page.getByRole("navigation", { name: "Ana menü", exact: true });
const link = (page, name) => menu(page).getByRole("link", { name, exact: true });

async function setup(page, { role, populated = false } = {}) {
  if (role) await page.addInitScript(role => {
    sessionStorage.setItem("travela_token", "navigation-test-token");
    sessionStorage.setItem("travela_user", JSON.stringify({ fullName: "Test Yolcu", role }));
  }, role);
  const calls = { lookups: [], mutations: [] };
  await page.route("**/api/**", async route => {
    const request = route.request(); const path = new URL(request.url()).pathname;
    if (request.method() !== "GET") calls.mutations.push(path);
    if (path === "/api/auth/login") return route.fulfill({ json: { token: "navigation-test-token", user: { fullName: "Test Yolcu", role: "Passenger" } } });
    if (path === "/api/profile") return route.fulfill({ json: { id: 1, fullName: "Test Yolcu", email: "test@example.test", role: "Passenger", createdAt: "2026-11-01T07:00:00Z", lastLoginAt: null } });
    if (path === "/api/profile/travel-summary") return route.fulfill({ json: { upcomingCount: 0, upcomingTrip: null, notifications: [] } });
    if (path === "/api/routes/city-options") return route.fulfill({ json: cities });
    if (path === "/api/tickets/mine" || path === "/api/reservations/mine") {
      expect(request.headers().authorization).toBe("Bearer navigation-test-token");
      return route.fulfill({ json: path === "/api/tickets/mine" && populated ? [{ ...ticket, pnr: "B".repeat(32), passengerName: "Hesap Yolcusu" }] : [] });
    }
    if (path === "/api/ticket-lookup") {
      expect(request.headers().authorization).toBeUndefined();
      calls.lookups.push(request.postDataJSON());
      return route.fulfill({ json: { ticket } });
    }
    return route.fulfill({ json: [] });
  });
  return calls;
}

async function expectMenu(page, passenger) {
  await expect(menu(page).getByRole("link")).toHaveText([
    "Ana Sayfa", "Sefer Ara", passenger ? "Biletlerim" : "Biletimi Sorgula", "Kampanyalar",
  ]);
  await expect(link(page, passenger ? "Biletimi Sorgula" : "Biletlerim")).toHaveCount(0);
  for (const item of await menu(page).getByRole("link").all()) await expect(item).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(await page.evaluate(() => document.documentElement.clientWidth) + 1);
}

for (const width of [390, 1440]) {
  test(`Oturumsuz menü, aktif bağlantılar ve kayıt/giriş işlemleri · ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 950 }); await setup(page); await page.goto("/");
    await expectMenu(page, false);
    await page.getByRole("banner").screenshot({ path: `../tmp/passenger-navigation/guest-${width}.png` });
    await expect(link(page, "Ana Sayfa")).toHaveAttribute("aria-current", "page");
    await link(page, "Sefer Ara").click(); await expect(page).toHaveURL(/\/#sefer-ara$/);
    await expect(link(page, "Sefer Ara")).toHaveAttribute("aria-current", "page");
    await expect(link(page, "Ana Sayfa")).not.toHaveAttribute("aria-current");
    await link(page, "Biletimi Sorgula").click(); await expect(page).toHaveURL(/\/bilet-sorgula$/);
    await expect(link(page, "Biletimi Sorgula")).toHaveAttribute("aria-current", "page");
    await page.getByRole("banner").getByRole("button", { name: "Kayıt Ol", exact: true }).click();
    await expect(page).toHaveURL(/\/register$/);
    await page.goto("/"); await page.getByRole("banner").getByRole("button", { name: "Giriş Yap", exact: true }).click();
    await expect(page).toHaveURL(/\/login$/);
  });

  test(`Giriş ve aynı ana sayfadan çıkış menüyü yenilemeden değiştirir · ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 950 }); await setup(page); await page.goto("/");
    await page.getByRole("banner").getByRole("button", { name: "Giriş Yap", exact: true }).click();
    await page.getByLabel("E-posta", { exact: true }).fill("test@example.test");
    await page.getByLabel("Şifre", { exact: true }).fill("TestPass123!");
    await page.getByRole("button", { name: "Giriş Yap", exact: true }).click();
    await expect(page).toHaveURL(/\/$/);
    await expectMenu(page, true);
    await expect(page.getByRole("banner").getByRole("link", { name: "Profilim", exact: true })).toBeVisible();
    await link(page, "Ana Sayfa").click(); await page.reload(); await expectMenu(page, true);
    await page.getByRole("banner").getByRole("button", { name: "Çıkış Yap", exact: true }).click();
    await expect(page).toHaveURL(/\/$/); await expectMenu(page, false);
    await expect(page.getByRole("banner").getByRole("button", { name: "Giriş Yap", exact: true })).toBeVisible();
    expect(await page.evaluate(() => sessionStorage.getItem("travela_token"))).toBeNull();
    await page.reload(); await expectMenu(page, false);
  });

  for (const populated of [false, true]) test(`Yolcu PNR bağlantısı ${populated ? "dolu" : "boş"} listede ve doğrudan adreste çalışır · ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 950 }); const calls = await setup(page, { role: "Passenger", populated });
    await page.goto("/tickets"); await expectMenu(page, true);
    await expect(link(page, "Biletlerim")).toHaveAttribute("aria-current", "page");
    if (populated) await expect(page.locator(".ticket-compact-list").getByRole("link", { name: "Bilet detayını gör", exact: true })).toHaveAttribute("href", `/tickets/${"B".repeat(32)}`);
    else await expect(page.getByText("Henüz biletiniz veya ödeme işleminiz yok.")).toBeVisible();
    const helper = page.getByRole("main").getByRole("link", { name: "PNR ile sorgula", exact: true });
    await page.screenshot({ path: `../tmp/passenger-navigation/tickets-${populated ? "full" : "empty"}-${width}.png`, fullPage: true });
    await expect(helper.locator("..")).toHaveText("Biletin burada görünmüyor mu? PNR ile sorgula");
    await expect(helper).toHaveAttribute("href", "/bilet-sorgula"); await helper.click();
    await expect(page).toHaveURL(/\/bilet-sorgula$/); await expectMenu(page, true);
    await page.reload(); await expectMenu(page, true);
    await expect(page.getByRole("heading", { name: "Biletimi Sorgula", exact: true })).toBeVisible();
    // Existing PNR + purchase contact are both required, even with a passenger session.
    for (const label of ["PNR numarası", "Cep telefonu veya e-posta"]) await expect(page.getByLabel(label, { exact: true })).toHaveAttribute("required", "");
    await page.getByLabel("PNR numarası", { exact: true }).fill(ticket.pnr);
    await page.getByLabel("Cep telefonu veya e-posta", { exact: true }).fill("guest@example.test");
    await page.getByRole("button", { name: "Biletimi Sorgula", exact: true }).click();
    await expect(page.getByRole("region", { name: "Sorgulanan bilet" })).toContainText("Misafir Yolcu");
    expect(calls.lookups).toEqual([{ pnr: ticket.pnr, contact: "guest@example.test" }]);
    expect(calls.mutations).toEqual(["/api/ticket-lookup"]);
    await link(page, "Biletlerim").click();
    await expect(page.getByRole("main").getByRole("link", { name: "Bilet detayını gör", exact: true })).toHaveCount(populated ? 1 : 0);
    await expect(page.getByRole("main").locator(`a[href="/tickets/${ticket.pnr}"]`)).toHaveCount(0);
    await expect(page.getByRole("main").getByRole("link", { name: "PNR ile sorgula", exact: true })).toBeVisible();
  });

  for (const role of ["Admin", "Company", "Driver"]) test(`${role} genel menüsü korunur · ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 950 }); await setup(page, { role }); await page.goto("/");
    await expectMenu(page, false);
    const banner = page.getByRole("banner");
    await expect(banner.getByRole("button", { name: "Çıkış Yap", exact: true })).toBeVisible();
    await expect(banner.getByRole("link", { name: "Profilim", exact: true })).toHaveCount(0);
    if (role !== "Driver") await expect(banner.getByRole("button", { name: role === "Admin" ? "Yönetici Paneli" : "Firma Paneli", exact: true })).toBeVisible();
  });
}

test("Oturumsuz bilet listesi ve bozuk oturum mevcut korumalardan geçemez", async ({ page }) => {
  await setup(page); await page.goto("/tickets"); await expect(page).toHaveURL(/\/login\?returnTo=%2Ftickets$/);
  await page.evaluate(() => { sessionStorage.setItem("travela_token", "invalid"); sessionStorage.setItem("travela_user", "invalid-json"); });
  await page.goto("/"); await expectMenu(page, false);
  await page.goto("/tickets"); await expect(page).toHaveURL(/\/login\?returnTo=%2Ftickets$/);
});
