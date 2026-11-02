import { test, expect } from "@playwright/test";
import { readFileSync, mkdirSync } from "node:fs";
import path from "node:path";

// These are synthetic responses recorded from the authenticated SQL/HTTP regression suite.
// Backend authorization is tested there; this suite isolates real React/CSS layout and printing.
const directory = path.resolve("../tmp/ticket-qa");
const fixtures = Object.fromEntries(["active", "pending", "failed", "cancelled"].map(state => [state, JSON.parse(readFileSync(path.join(directory, state + ".json"), "utf8"))]));
mkdirSync(path.join(directory, "screenshots"), { recursive: true });

async function prepare(page, state, viewport) {
  await page.setViewportSize(viewport);
  await page.addInitScript(() => {
    sessionStorage.setItem("travela_token", "isolated-layout-test-token");
    sessionStorage.setItem("travela_user", JSON.stringify({ role: "Passenger", fullName: "Çağrı Test" }));
    window.print = () => { window.__ticketPrinted = true; };
  });
  let current = state;
  let reads = 0;
  await page.route("**/api/tickets/*", async route => {
    reads++;
    await route.fulfill({ status: 200, contentType: "application/json", headers: { "Cache-Control": "private, no-store" }, body: JSON.stringify(fixtures[current]) });
  });
  await page.goto(`/tickets/${fixtures[state].pnr}`);
  await expect(page.getByRole("heading", { name: "Bilet detayı", exact: true })).toBeVisible();
  return { setState: value => { current = value; }, readCount: () => reads };
}

async function noHorizontalOverflow(page) {
  const metrics = await page.evaluate(() => {
    const viewport = document.documentElement.clientWidth;
    const visible = [...document.querySelectorAll(".ticket-document, .ticket-document-actions *, .site-header a, .site-header button")]
      .filter(element => element.getClientRects().length > 0)
      .map(element => ({ text: element.textContent?.trim(), rect: element.getBoundingClientRect() }))
      .filter(item => item.rect.left < -1 || item.rect.right > viewport + 1)
      .map(item => item.text);
    return { viewport, pageWidth: document.documentElement.scrollWidth, outside: visible };
  });
  expect(metrics.pageWidth).toBeLessThanOrEqual(metrics.viewport + 1);
  expect(metrics.outside).toEqual([]);
}

for (const width of [360, 390, 1440]) {
  for (const state of ["active", "cancelled"]) {
    test(`${state}: ${width}px, uzun ad/PNR, durumlar ve butonlar`, async ({ page }) => {
      await prepare(page, state, { width, height: 900 });
      const item = fixtures[state];
      await expect(page.locator(".ticket-pnr")).toContainText(item.pnr);
      await expect(page.locator(".ticket-document-facts")).toContainText(item.passengerName);
      await expect(page.getByText("TEST BİLETİ — Gerçek seyahat için geçerli değildir", { exact: true })).toBeVisible();
      await expect(page.getByRole("button", { name: "PDF indir", exact: true })).toBeVisible();
      await expect(page.getByRole("button", { name: "Yazdır", exact: true })).toBeVisible();
      if (state === "active") await expect(page.locator(".ticket-refund-explanation")).toHaveCount(0);
      if (state === "active") await expect(page.locator(".ticket-cancelled-banner")).toHaveCount(0);
      else {
        await expect(page.locator(".ticket-cancelled-banner")).toHaveText("İPTAL EDİLDİ");
        await expect(page.locator(".ticket-history")).toContainText(item.cancellationReason);
        await expect(page.locator(".ticket-refund-explanation")).toContainText("İadeniz tamamlandı.");
        await expect(page.locator(".ticket-refund-explanation")).not.toContainText("sonuç bekleniyor");
      }
      await noHorizontalOverflow(page);
      await page.screenshot({ path: path.join(directory, "screenshots", `${state}-${width}.png`), fullPage: true });
    });
  }
}

for (const state of ["pending", "failed"]) {
  test(`${state}: güncel iade açıklaması geçmiş iptal nedeninden ayrı`, async ({ page }) => {
    await prepare(page, state, { width: 390, height: 900 });
    await expect(page.locator(".ticket-history")).toContainText(fixtures[state].cancellationReason);
    await expect(page.locator(".ticket-refund-explanation")).toContainText(state === "pending" ? "İadeniz bekleniyor." : "İade tamamlanamadı.");
    await expect(page.locator(".ticket-refund-explanation")).not.toContainText("İadeniz tamamlandı.");
    if (state === "failed") { await page.getByText("İade açıklaması", { exact: true }).click(); await expect(page.locator(".ticket-refund-explanation")).toContainText(fixtures.failed.lastError); }
    await noHorizontalOverflow(page);
  });
}

test("Yazdırma önce güncel sunucu durumunu alır; menü ve tüm işlem butonları gizlenir", async ({ page }) => {
  const control = await prepare(page, "active", { width: 1280, height: 900 });
  control.setState("cancelled");
  await page.getByRole("button", { name: "Yazdır", exact: true }).click();
  await expect.poll(() => page.evaluate(() => window.__ticketPrinted === true)).toBe(true);
  expect(control.readCount()).toBeGreaterThanOrEqual(2);
  await expect(page.locator(".ticket-cancelled-banner")).toBeVisible();
  await page.emulateMedia({ media: "print" });
  await expect(page.locator(".site-header")).toBeHidden();
  await expect(page.locator(".ticket-document-actions")).toBeHidden();
  await expect(page.locator("button:visible")).toHaveCount(0);
  await expect(page.locator(".ticket-refund-explanation")).toContainText("İadeniz tamamlandı.");
  await page.screenshot({ path: path.join(directory, "screenshots", "cancelled-print.png"), fullPage: true });
});
