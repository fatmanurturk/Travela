import { test, expect } from "@playwright/test";
const rows = Array.from({ length: 12 }, (_, i) => ({ id: i + 1, companyId: 1, companyName: "İstanbul Seyahat", busId: i + 1, busPlateNumber: `34 TEST ${i + 1}`, busCapacity: 20, routeId: 1, departureCity: "İstanbul", arrivalCity: "Ankara", departureTime: `2099-01-${String(i + 1).padStart(2, "0")}T07:00:00Z`, arrivalTime: `2099-01-${String(i + 1).padStart(2, "0")}T13:00:00Z`, createdAt: i === 0 ? null : `2026-11-24T${String(i).padStart(2, "0")}:00:00Z`, price: 550, availableSeats: 17, status: "Scheduled" }));
async function setup(page, role = "Admin") {
  const state = { lists: [], exports: [], fail: false, empty: false, release: null, block: false };
  await page.addInitScript(role => { sessionStorage.setItem("travela_token", "export-test"); sessionStorage.setItem("travela_user", JSON.stringify({ role, fullName: "Yetkili" })); }, role);
  await page.route("**/api/**", async route => {
    const request = route.request(), url = new URL(request.url()), path = url.pathname;
    if (path === "/api/company/profile") return route.fulfill({ json: { name: "Test Firma" } });
    if (path === "/api/admin/companies") return route.fulfill({ json: [{ id: 1, name: "İstanbul Seyahat", isActive: true }] });
    if (path === "/api/buses" || path === "/api/routes" || path === "/api/terminals") return route.fulfill({ json: [] });
    if (path === "/api/trips/summary") return route.fulfill({ json: { totalTrips: 12, scheduledTrips: 12, inProgressTrips: 0, completedTrips: 0, cancelledTrips: 0 } });
    if (path === "/api/trips/export") {
      expect(request.headers().authorization).toBe("Bearer export-test"); state.exports.push(Object.fromEntries(url.searchParams));
      if (state.block) await new Promise(resolve => { state.release = resolve; });
      if (state.fail) return route.fulfill({ status: 500, json: { message: "Excel hazırlanamadı. Yeniden deneyin." } });
      return route.fulfill({ body: Buffer.from("workbook-download-fixture"), contentType: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", headers: { "Content-Disposition": "attachment; filename=seferler_2026-11-24_15-30.xlsx" } });
    }
    if (path === "/api/trips") {
      const query = Object.fromEntries(url.searchParams); state.lists.push(query); const all = state.empty ? [] : [...rows];
      if (query.sortBy === "createdAt") all.sort((a, b) => a.createdAt == null ? 1 : b.createdAt == null ? -1 : a.createdAt.localeCompare(b.createdAt) * (query.sortDirection === "desc" ? -1 : 1));
      const pageNumber = Number(query.pageNumber ?? 1); return route.fulfill({ json: { items: all.slice((pageNumber - 1) * 10, pageNumber * 10), totalCount: all.length, totalPages: Math.ceil(all.length / 10) } });
    }
    return route.fulfill({ json: [] });
  }); return state;
}
for (const role of ["Admin", "Company"]) for (const width of [390, 1440]) test(`${role} oluşturma filtresi, sıralama ve Excel aktarımı: ${width}px`, async ({ page }) => {
  await page.setViewportSize({ width, height: 1000 }); const state = await setup(page, role); await page.goto(role === "Admin" ? "/admin/trips" : "/company/trips");
  const period = page.getByRole("combobox", { name: "Oluşturma tarihi", exact: true }); const exportButton = page.getByRole("button", { name: "Excel’e Aktar", exact: true });
  await expect(period).toHaveValue("all"); await expect(page.getByText("Aktarılacak: 12 sefer", { exact: true })).toBeVisible(); await expect(page.getByRole("cell", { name: "Bilinmiyor", exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Sonraki", exact: true }).click(); await expect(page.getByText("Sayfa 2 / 2", { exact: true })).toBeVisible();
  for (const value of ["today", "yesterday", "last7days", "thismonth"]) { await period.selectOption(value); await expect.poll(() => state.lists.at(-1)?.createdPeriod).toBe(value); await expect(page.getByText("Sayfa 1 / 2", { exact: true })).toBeVisible(); }
  await period.selectOption("custom"); await expect(page.getByRole("alert")).toContainText("başlangıç ve bitiş"); await expect(exportButton).toBeDisabled();
  await page.getByLabel("Oluşturma başlangıç tarihi", { exact: true }).fill("2026-11-24"); await page.getByLabel("Oluşturma bitiş tarihi", { exact: true }).fill("2026-11-23"); await expect(page.getByRole("alert")).toContainText("sonra olamaz"); await expect(exportButton).toBeDisabled();
  await page.getByLabel("Oluşturma bitiş tarihi", { exact: true }).fill("2026-11-24"); await expect(exportButton).toBeEnabled();
  await page.getByLabel("Kalkış şehri", { exact: true }).fill("İstanbul");
  if (role === "Admin") await page.getByRole("combobox", { name: "Firma", exact: true }).selectOption("1");
  else await expect(page.getByRole("combobox", { name: "Firma", exact: true })).toHaveCount(0);
  await page.getByLabel("En düşük fiyat", { exact: true }).fill("100");
  await page.getByRole("button", { name: "Oluşturulma", exact: false }).click(); await expect(page.getByRole("columnheader", { name: "Oluşturulma ↑" })).toHaveAttribute("aria-sort", "ascending");
  await page.getByRole("button", { name: "Oluşturulma ↑", exact: true }).click(); await expect(page.getByRole("columnheader", { name: "Oluşturulma ↓" })).toHaveAttribute("aria-sort", "descending"); await expect(exportButton).toBeEnabled();
  await page.getByRole("button", { name: "Sonraki", exact: true }).click(); await expect(page.getByText("Sayfa 2 / 2", { exact: true })).toBeVisible(); await expect(exportButton).toBeEnabled();
  state.block = true; const download = page.waitForEvent("download"); await exportButton.click(); await expect(page.getByRole("button", { name: "Excel hazırlanıyor…", exact: true })).toBeDisabled();
  expect(state.exports).toHaveLength(1); expect(state.exports[0]).toMatchObject({ ...(role === "Admin" ? { companyId: "1" } : {}), departureCity: "İstanbul", minPrice: "100", createdPeriod: "custom", createdFrom: "2026-11-24", createdTo: "2026-11-24", sortBy: "createdAt", sortDirection: "desc" });
  if (role === "Company") expect(state.exports[0]).not.toHaveProperty("companyId");
  expect(state.exports[0]).not.toHaveProperty("pageNumber"); expect(state.exports[0]).not.toHaveProperty("pageSize");
  await expect.poll(() => !!state.release).toBe(true); state.release(); state.block = false; expect((await download).suggestedFilename()).toBe("seferler_2026-11-24_15-30.xlsx"); await expect(exportButton).toBeEnabled();
  state.fail = true; await exportButton.click(); await expect(page.getByRole("alert")).toContainText("Yeniden deneyin"); await expect(exportButton).toBeEnabled(); state.fail = false;
  await page.getByRole("button", { name: "Filtreleri Temizle", exact: true }).click(); await expect(period).toHaveValue("all"); await expect(page.getByLabel("Oluşturma başlangıç tarihi", { exact: true })).toHaveCount(0); await expect.poll(() => state.lists.at(-1)?.sortBy).toBe("date"); await expect(page.getByText("Sayfa 1 / 2", { exact: true })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width + 1); await page.screenshot({ path: `../tmp/trip-export/${role.toLowerCase()}-${width}.png`, fullPage: true });
  state.empty = true; await page.getByLabel("Kalkış şehri", { exact: true }).fill("Yok"); await expect(page.getByText("Aktarılacak: 0 sefer", { exact: true })).toBeVisible(); await expect(exportButton).toBeDisabled();
});
