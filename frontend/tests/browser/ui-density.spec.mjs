import { test, expect } from "@playwright/test";
import { buildSeats } from "../../src/seatPlans.ts";
test.describe.configure({ mode: "parallel" });

// Synthetic data only: every API request is intercepted, including form submissions.
const companyName = "Uzun İsimli Şehirlerarası Yolcu Taşımacılığı ve Turizm Şirketi";
const description = "Terminal değişikliği ve yolculuk bilgileri hakkında uzun açıklama. ".repeat(5);
const createdAt = "2026-01-01T07:00:00Z";
const departureTime = "2099-06-21T07:00:00Z", arrivalTime = "2099-06-21T13:00:00Z";
const company = { id: 1, name: companyName, email: "iletisim@uzunsirketadi.example.test", phone: "+905321234567", address: description, isActive: true, createdAt };
const user = { id: 7, fullName: "Ayşe Nur Uzun Soyadlı Kullanıcı", email: "ayse.nur@uzunepostaadresi.example.test", phoneNumber: "+905321234567", role: "Passenger", isActive: true, companyId: 1, companyName, createdAt, lastLoginAt: createdAt, notificationPreferences: {} };
const routeData = { id: 1, departureCity: "Afyonkarahisar", arrivalCity: "Kahramanmaraş", estimatedDurationMinutes: 360, distanceKm: 450, tripCount: 1, plannedTripCount: 1, hasTrips: true, isActive: true };
const bus = { id: 1, companyId: 1, companyName, plateNumber: "34 TRV 041", brand: "Mercedes", model: "Travego", seatCapacity: 41, seatLayout: "2+1", seatPlanJson: JSON.stringify(buildSeats("2+1", 41)), isActive: true, createdAt };
const trip = { id: 1, companyId: 1, companyName, busId: 1, busPlateNumber: bus.plateNumber, busCapacity: 41, routeId: 1,
  departureCity: routeData.departureCity, arrivalCity: routeData.arrivalCity, departureTime, arrivalTime, durationMinutes: 360,
  price: 1250, availableSeats: 39, seatCapacity: 41, seatLayout: "2+1", seats: buildSeats("2+1", 41, [41]), occupiedSeats: [3],
  seatOccupancies: [{ seatNumber: 3, gender: "Female", kind: "Ticket", expiresAt: null }], canReserve: true, seatPlanAvailable: true,
  departureHasPassed: false, status: "Scheduled", createdAt, companyIsActive: true, busIsActive: true,
  departureTerminalName: "Şehirlerarası Otobüs Terminali Uzun İsimli Biniş Noktası", arrivalTerminalName: "Merkez Otobüs Terminali", departureTerminalAddress: description, arrivalTerminalAddress: null, boardingInstructions: description };
const id = "12345678-1234-1234-1234-123456789012";
const ticket = { id, purchaseGroupId: id, tripId: 1, companyName, pnr: "TRAVELATEST12345678901234567890123", kind: "Ticket", passengerName: user.fullName, passengerGender: "Female", passengerEmail: user.email, passengerPhone: user.phoneNumber,
  departureCity: trip.departureCity, arrivalCity: trip.arrivalCity, departureTime, arrivalTime, seatNumber: 22, amount: 1250, originalAmount: 1250, discountAmount: 0, currency: "TRY", isTest: true, paymentStatus: "Paid", ticketStatus: "Active", refundStatus: "None", refundAmount: 0, canCancel: true, cancelDeadline: "2099-06-21T05:00:00Z", paidAt: createdAt, retrievedAt: createdAt, createdAt };
const metrics = { sold: 1, active: 1, cancelled: 0, collected: 1250, refunded: 0, pendingRefund: 0, failedRefundCount: 0, failedRefundAmount: 0, collectedMinusRefunded: 1250 };
const reportTrip = { ...metrics, id: 1, routeId: 1, departureCity: trip.departureCity, arrivalCity: trip.arrivalCity, departureTime, plate: bus.plateNumber, capacity: 41, activeSoldSeats: 1, heldSeats: 0, legacyBlockedSeats: 0, otherEnvironmentBlocks: 0, expiredHolds: 0, availableForSale: 40, occupancyPercent: 2.44, dataWarning: null, status: "Scheduled" };
const report = { scope: { from: "2026-11-01", to: "2026-11-18", basis: "transaction", environment: "all", generatedAt: createdAt, dateExplanation: description }, metrics, occupancy: { activeSoldSeats: 1, capacity: 41, tripCount: 1, invalidTrips: 0, percent: 2.44 }, daily: [{ date: "2026-11-01", ...metrics }], routes: [{ ...metrics, routeId: 1, departureCity: trip.departureCity, arrivalCity: trip.arrivalCity }], routeCount: 1, trips: [reportTrip], totalTrips: 1, upcoming: [reportTrip], recent: [], failedRefunds: [], unknownRefundDateCount: 0, unknownRefundDateAmount: 0 };
const campaign = { id: 1, companyId: 1, companyName, routeId: null, routeName: null, name: "Uzun Başlıklı Yolculuk Avantajı Kampanyası", description, code: "YOLCULUK2026", discountType: "Percentage", value: 10, minimumSubtotal: 100, maximumDiscount: 500, usageLimit: 100, perUserLimit: 1, startsAt: createdAt, endsAt: departureTime, departureFrom: null, departureUntil: null, isActive: true, isPublic: true, version: "v1", used: 0, reserved: 0, totalDiscount: 0 };

async function setup(page, role = "Passenger", state = "loaded") {
  await page.emulateMedia({ reducedMotion: "reduce" });
  await page.addInitScript(({ role, user }) => {
    sessionStorage.setItem("travela_token", "isolated-density-test");
    sessionStorage.setItem("travela_user", JSON.stringify({ ...user, role }));
    sessionStorage.setItem("travela_pending_email", user.email);
  }, { role, user });
  await page.route("**/api/**", async r => {
    const path = new URL(r.request().url()).pathname;
    if (state === "loading") return; // Held until the page/context is closed.
    if (state === "error") return r.fulfill({ status: 500, json: { message: description } });
    const list = items => state === "empty" ? [] : items;
    const paged = items => ({ items: list(items), totalCount: state === "empty" ? 0 : items.length, totalPages: 1, pageNumber: 1, pageSize: 10 });
    let data;
    if (r.request().method() !== "GET" && path !== "/api/campaigns/quote") return r.fulfill({ status: 400, json: { message: description } });
    if (path === "/api/profile" || path === "/api/company/user") data = { ...user, role };
    else if (path === "/api/profile/travel-summary") data = { upcomingCount: 0, upcomingTrip: null, notifications: [] };
    else if (["/api/company/profile", "/api/company/me"].includes(path)) data = company;
    else if (path === "/api/company/me/summary") data = { totalBuses: 1, activeBuses: 1, totalTrips: 1, activeTrips: 1 };
    else if (path === "/api/admin/dashboard") data = { totalCompanies: 1, totalUsers: 1, totalBuses: 1, activeUsers: 1, inactiveUsers: 0, adminUsers: 1, companyUsers: 1, passengerUsers: 1 };
    else if (path === "/api/admin/companies") data = list([company]);
    else if (path === "/api/admin/companies/1") data = { ...company, totalUsers: 1, totalBuses: 1, users: [user], buses: [bus] };
    else if (path === "/api/admin/users") data = paged([user]);
    else if (path === "/api/buses/summary") data = { totalBuses: 1, activeBuses: 1, inactiveBuses: 0, companiesWithBuses: 1 };
    else if (path === "/api/buses") data = paged([bus]);
    else if (path === "/api/buses/1") data = { ...bus, trips: [] };
    else if (path === "/api/trips/summary") data = { totalTrips: 1, scheduledTrips: 1, inProgressTrips: 0, completedTrips: 0, cancelledTrips: 0 };
    else if (path === "/api/trips") data = paged([trip]);
    else if (path === "/api/trips/search") data = { ...paged([trip]), companies: [{ id: 1, name: companyName }] };
    else if (path === "/api/trips/upcoming") data = list([trip]);
    else if (path === "/api/trips/featured-routes") data = list([{ ...routeData, routeId: 1, startingPrice: 1250, upcomingTripCount: 1 }]);
    else if (path === "/api/trips/public/1") data = trip;
    else if (["/api/routes", "/api/routes/public"].includes(path)) data = list([routeData]);
    else if (path === "/api/terminals") data = list([{ id: 1, name: trip.departureTerminalName, city: trip.departureCity, address: description, isActive: true }]);
    else if (["/api/tickets/mine", "/api/reservations/mine"].includes(path)) data = list(path.includes("tickets") ? [ticket] : []);
    else if (path.endsWith("/communications")) data = { changes: [], notifications: [] };
    else if (path.startsWith("/api/tickets/")) data = ticket;
    else if (path === "/api/purchases/options") data = { provider: "LocalTest", requiresBuyer: false };
    else if (path.startsWith("/api/purchases/")) data = { id, amount: 1250, originalAmount: 1250, discountAmount: 0, provider: "LocalTest", isTest: true, paymentStatus: "Paid", checkoutState: "Ready", holdExpiresAt: departureTime, refundRequested: 0, refunded: 0, legalAccepted: true, tickets: [ticket] };
    else if (path === "/api/campaigns/quote") data = { originalAmount: 1250, discountAmount: 0, amount: 1250 };
    else if (path === "/api/campaigns/options") data = { companies: [company], routes: [{ id: 1, name: "Afyonkarahisar → Kahramanmaraş" }] };
    else if (["/api/campaigns/public", "/api/campaigns"].includes(path)) data = list([campaign]);
    else if (path === "/api/travel-notifications") data = paged([{ id, type: "TripChanged", channel: "Email", recipient: user.email, pnr: ticket.pnr, status: "Failed", attempts: 1, lastError: description, createdAt }]);
    else if (path === "/api/reservations/company") data = paged([{ ...ticket, reservationNumber: ticket.pnr, price: 1250, status: "Active" }]);
    else if (path === "/api/admin/tickets") data = paged([{ ...ticket, orderId: id, passengerId: 7, accountName: user.fullName, reservationNumber: ticket.pnr, companyId: 1 }]);
    else if (path === "/api/company/reports/options") data = [routeData];
    else if (path === "/api/company/reports") data = report;
    else if (path === "/api/company/attention") data = { generatedAt: createdAt, groups: [] };
    else return r.fulfill({ status: 404, json: { message: "Kayıt bulunamadı." } });
    return r.fulfill({ json: data });
  });
}

async function verify(page, name, width) {
  await expect(page.locator("main, .login-card").first()).toBeVisible();
  const geometry = await page.evaluate(() => {
    const scrollAreas = ".table-wrap, .company-details-table-wrap, .campaign-table-wrap, .bus-scroll, .plan-table-scroll, .admin-menu, .company-menu, .trip-schedule-preview";
    const visible = e => e.getClientRects().length && getComputedStyle(e).visibility !== "hidden" &&
      (!e.closest("details:not([open])") || e.closest("summary"));
    const modal = document.querySelector(".modal-card");
    const controls = [...document.querySelectorAll("main button, main input, main select, main textarea, main .site-button, .login-card input, .login-card button")].filter(e => visible(e) && !e.closest(scrollAreas) && (!modal || modal.contains(e)));
    const outside = controls.filter(e => { const r = e.getBoundingClientRect(); return r.left < -1 || r.right > innerWidth + 1; }).map(e => e.getAttribute("aria-label") || e.textContent.trim());
    const clipped = [...document.querySelectorAll("main h1, main h2, main h3, main p, main dd, main button")].filter(e => {
      const style = getComputedStyle(e);
      // Visible font ascenders can extend a line box without being clipped. Check actual clipping.
      return visible(e) && !e.closest(scrollAreas) && e.clientWidth > 0 &&
        (e.scrollWidth > e.clientWidth + 2 || (["hidden", "clip"].includes(style.overflowY) && e.scrollHeight > e.clientHeight + 2));
    }).map(e => e.textContent.trim().slice(0, 90));
    const overlaps = controls.flatMap((a, i) => controls.slice(i + 1).filter(b => {
      if (a.contains(b) || b.contains(a) || a.closest(".account-password-input")?.contains(b)) return false;
      const x = a.getBoundingClientRect(), y = b.getBoundingClientRect();
      return x.left < y.right - 2 && x.right > y.left + 2 && x.top < y.bottom - 2 && x.bottom > y.top + 2;
    }).map(b => [a.textContent.trim().slice(0, 40), b.textContent.trim().slice(0, 40)]));
    return { pageWidth: document.documentElement.scrollWidth, viewport: innerWidth, outside, clipped, overlaps };
  });
  expect(geometry, name).toMatchObject({ outside: [], clipped: [], overlaps: [] });
  expect(geometry.pageWidth, name).toBeLessThanOrEqual(geometry.viewport + 1);
  await page.screenshot({ path: `../tmp/ui-density/${name}-${width}.png`, fullPage: true });
}

const publicRoutes = ["/", "/search?all=true", "/trips/1", "/campaigns", "/login", "/register", "/forgot-password", "/resend-activation", "/reset-password", "/activate-account", "/first-login-password-change", "/profile", "/tickets", `/tickets/${ticket.pnr}`, `/payment/${id}`, `/purchase/${id}`, "/hakkimizda", "/iletisim", "/yardim", "/gizlilik-ve-kvkk", "/kullanim-kosullari", "/mesafeli-satis-sozlesmesi", "/iade-ve-iptal", "/cerez-politikasi", "/olmayan-sayfa"];
const adminRoutes = ["/admin", "/admin/companies", "/admin/companies/1", "/admin/users", "/admin/buses", "/admin/trips", "/admin/routes", "/admin/tickets", "/admin/campaigns", "/admin/terminals", "/admin/notifications"];
const companyRoutes = ["/company", "/company/buses", "/company/trips", "/company/routes", "/company/reservations", "/company/reports", "/company/campaigns", "/company/notifications", "/company/profile"];
for (const width of [390, 768, 1440]) for (const [role, paths] of [["Passenger", publicRoutes], ["Admin", adminRoutes], ["Company", companyRoutes]]) {
  test(`${role} screen audit ${width}px: long text and reachable controls`, async ({ page }) => {
    test.setTimeout(120000);
    await page.setViewportSize({ width, height: 1000 });
    await setup(page, role);
    const errors = [];
    page.on("pageerror", e => errors.push(e.message));
    for (const path of paths) {
      await page.goto(path);
      await page.waitForLoadState("networkidle");
      await verify(page, `${role}-${path.replace(/[^a-z0-9]/gi, "-")}`, width);
      expect(errors, path).toEqual([]);
      if (path === "/trips/1") {
        await expect(page.locator(".seat-booking > aside")).toHaveCount(0);
        await expect(page.locator(".passenger-checkout-form fieldset")).toHaveCount(0);
        if (width === 1440) expect((await page.locator(".trip-summary-card").boundingBox()).height).toBeLessThan(240);
      }
      if (path === "/search?all=true" && width === 390) {
        await expect(page.getByRole("combobox", { name: "Firma", exact: true })).toBeHidden();
        await page.getByRole("button", { name: "Filtreleri göster" }).click();
        await expect(page.getByRole("combobox", { name: "Firma", exact: true })).toBeVisible();
        await verify(page, "search-expanded", width);
      }
      if (path === "/admin/companies") {
        await page.getByRole("button", { name: "+ Yeni Firma Ekle" }).click();
        await verify(page, "company-create-form", width);
      }
      if (path === "/admin/users") {
        await page.locator("tbody").getByRole("button", { name: "Detay", exact: true }).first().click();
        const modal = page.locator(".modal-card");
        await expect(modal).toBeVisible();
        await verify(page, "user-detail-modal", width);
        for (const button of await modal.getByRole("button").all()) {
          await button.scrollIntoViewIfNeeded();
          expect(await button.evaluate(e => {
            const r = e.getBoundingClientRect();
            return e.contains(document.elementFromPoint(r.x + r.width / 2, r.y + r.height / 2));
          })).toBe(true);
        }
      }
    }
  });
}

for (const width of [390, 768, 1440]) for (const state of ["empty", "error", "loading"]) test(`${state} screen states ${width}px`, async ({ page }) => {
  test.setTimeout(60000);
  await page.setViewportSize({ width, height: 1000 });
  await setup(page, "Passenger", state);
  for (const path of ["/", "/search?all=true", "/campaigns", "/trips/1", "/tickets", "/profile"]) {
    await page.goto(path);
    if (state !== "loading") await page.waitForLoadState("networkidle");
    else await expect(page.getByText(/yükleniyor|aranıyor/i).first()).toBeVisible();
    await verify(page, `${state}-${path.replace(/[^a-z0-9]/gi, "-")}`, width);
  }
});
