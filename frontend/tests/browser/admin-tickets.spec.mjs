import { test, expect } from "@playwright/test";

const ticket = { id: 41, orderId: "12345678-1234-1234-1234-123456789012", reservationNumber: "REC-41", pnr: "PNR123456", kind: "Ticket", tripId: 9,
  companyId: 3, companyName: "Merkezi Firma", passengerId: 8, accountName: "Güncel Profil Adı", passengerName: "Biletteki Yolcu",
  passengerEmail: "old@example.com", passengerPhone: "+905321234567", passengerSnapshotSource: "Checkout", passengerCapturedAt: "2026-11-01T08:00:00Z",
  departureCity: "İstanbul", arrivalCity: "Ankara", departureTime: "2099-11-10T08:00:00Z", seatNumber: 12, amount: 600,
  paymentStatus: "Paid", refundStatus: "Failed", ticketStatus: "Cancelled", isTest: true, needsAttention: true, createdAt: "2026-11-01T08:00:00Z" };
const payment = { provider: "LocalTest", providerReference: "TEST-PAYMENT", paidAt: "2026-11-01T08:00:00Z", refundAmount: 600, refundAttempts: 8,
  refundReference: null, refundRequestedAt: "2026-11-02T08:00:00Z", refundedAt: null, nextRefundAttemptAt: null, lastReconciledAt: null,
  nextReconciliationAt: null, reconciliationAttempts: 0, lastError: "İade işlemi tamamlanamadı.", canRetryRefund: true, canReconcile: false };

async function setup(page, { pending = false, legacy = false } = {}) {
  await page.addInitScript(() => { sessionStorage.setItem("travela_token", "admin-operations-token"); sessionStorage.setItem("travela_user", JSON.stringify({ role: "Admin" })); });
  let current = { ...ticket, ...(pending ? { pnr: null, kind: "Hold", paymentStatus: "Pending", refundStatus: "None", ticketStatus: "Active" } : {}), ...(legacy ? { orderId: null, pnr: null, kind: "Legacy", passengerSnapshotSource: "Migration", paymentStatus: "Unpaid", refundStatus: "None" } : {}) };
  const requests = [];
  await page.route("**/api/admin/companies", route => route.fulfill({ json: [{ id: 3, name: "Merkezi Firma" }] }));
  await page.route("**/api/admin/tickets?*", route => { requests.push(new URL(route.request().url())); return route.fulfill({ json: { items: [current], totalCount: 1, pageNumber: 1, pageSize: 20 } }); });
  await page.route(/\/api\/admin\/tickets\/41\?/, route => route.fulfill({ json: { ticket: current,
    cancellation: { cancelledAt: current.ticketStatus === "Cancelled" ? "2026-11-02T08:00:00Z" : null, cancellationReason: null },
    payment: legacy ? null : { ...payment, ...(pending ? { canRetryRefund: false, canReconcile: true } : {}) },
    history: { items: legacy ? [] : [{ id: 1, type: "RefundFailed", createdAt: "2026-11-02T08:00:00Z", paymentStatus: current.paymentStatus, refundStatus: current.refundStatus, refundAmount: 600, actorName: null, actorUserId: null, reason: null }], totalCount: legacy ? 0 : 1, pageNumber: 1, pageSize: 20 }
  } }));
  return { requests, update: values => { current = { ...current, ...values }; } };
}

test("Yönetici menüsü merkezi biletlere gider; arama ve filtreler API'ye uygulanır", async ({ page }) => {
  const { requests } = await setup(page);
  await page.goto("/admin/tickets");
  await expect(page.getByRole("heading", { name: "Bilet ve ödeme yönetimi", exact: true })).toBeVisible();
  await page.getByRole("searchbox").fill("PNR123456");
  await page.getByRole("combobox", { name: "Firma", exact: true }).selectOption("3");
  await page.getByRole("combobox", { name: "Destek takibi", exact: true }).selectOption("true");
  await page.getByRole("button", { name: "Ara", exact: true }).click();
  await expect(page.getByRole("status")).toHaveText("1 kayıt bulundu");
  expect(requests.at(-1).searchParams.get("search")).toBe("PNR123456");
  expect(requests.at(-1).searchParams.get("companyId")).toBe("3");
  expect(requests.at(-1).searchParams.get("needsAttention")).toBe("true");
  await page.getByRole("button", { name: "İncele", exact: true }).click();
  await expect(page).toHaveURL(/detail=41/);
  await expect(page.getByRole("heading", { name: "İşlem detayı #41", exact: true })).toBeVisible();
  await expect(page.locator(".admin-ticket-facts")).toContainText("Güncel Profil Adı");
  await expect(page.locator(".admin-ticket-facts")).toContainText("Biletteki Yolcu");
  await expect(page.locator(".admin-ticket-facts")).toContainText("old@example.com");
});

test("Bekleyen ödemede seçili işlem doğru sorgulama ucuna gider", async ({ page }) => {
  await setup(page, { pending: true }); let sent;
  await page.route("**/api/admin/tickets/41/reconcile", async route => { sent = route.request().postDataJSON(); await route.fulfill({ status: 202, json: { message: "Ödeme tekrar sorgulama kuyruğuna alındı." } }); });
  await page.goto("/admin/tickets?detail=41");
  await expect(page.getByRole("combobox", { name: "Yapılacak işlem", exact: true })).toHaveValue("reconcile");
  await page.getByLabel("İşlem gerekçesi", { exact: true }).fill("Kayıp bildirim için sağlayıcı kontrolü");
  await page.getByRole("button", { name: "Gerekçeyle kuyruğa al", exact: true }).click();
  await expect(page.locator(".admin-ticket-detail").getByRole("status")).toHaveText("Ödeme tekrar sorgulama kuyruğuna alındı.");
  expect(sent.reason).toBe("Kayıp bildirim için sağlayıcı kontrolü"); expect(sent.requestKey).toMatch(/^[a-f0-9-]{36}$/);
});

test("İade ağ hatasından sonra aynı gerekçeyle aynı işlem anahtarı kullanılır", async ({ page }) => {
  await setup(page); const sent = [];
  await page.route("**/api/admin/tickets/41/retry-refund", route => { sent.push(route.request().postDataJSON()); return sent.length === 1 ? route.abort("failed") : route.fulfill({ status: 202, json: { message: "İade yeniden denemeye alındı." } }); });
  await page.goto("/admin/tickets?detail=41");
  await page.getByLabel("İşlem gerekçesi", { exact: true }).fill("Sağlayıcı incelemesi tamamlandı, yeniden denensin");
  await page.getByRole("button", { name: "Gerekçeyle kuyruğa al", exact: true }).click();
  await expect(page.getByRole("alert")).toContainText("Yanıt alınamadı");
  await page.getByRole("button", { name: "Gerekçeyle kuyruğa al", exact: true }).click();
  await expect(page.locator(".admin-ticket-detail").getByRole("status")).toHaveText("İade yeniden denemeye alındı.");
  expect(sent).toHaveLength(2); expect(sent[0].requestKey).toBe(sent[1].requestKey);
  await expect(page.getByLabel("İşlem gerekçesi", { exact: true })).toHaveValue("");
});

for (const width of [390, 1440]) test(`Eski kayıt ve sabit yolcu açıklaması doğru gösterilir: ${width}px`, async ({ page }) => {
  await page.setViewportSize({ width, height: 1000 }); await setup(page, { legacy: true });
  await page.goto("/admin/tickets?detail=41");
  await expect(page.getByText("Bu kayıt için ödeme hareketi bulunmuyor.", { exact: true })).toBeVisible();
  await expect(page.locator(".admin-ticket-detail")).toContainText("satın alma anındaki özgün bilgiler doğrulanamamaktadır");
  await expect(page.getByRole("button", { name: "Gerekçeyle kuyruğa al", exact: true })).toHaveCount(0);
  const size = await page.evaluate(() => ({ width: document.documentElement.scrollWidth, viewport: document.documentElement.clientWidth }));
  expect(size.width).toBeLessThanOrEqual(size.viewport + 1);
});

test("Süresi dolan yönetici oturumu temizlenir", async ({ page }) => {
  await setup(page); await page.route("**/api/admin/tickets?*", route => route.fulfill({ status: 401, json: {} }));
  await page.goto("/admin/tickets"); await expect(page).toHaveURL(/\/login$/);
  expect(await page.evaluate(() => sessionStorage.getItem("travela_token"))).toBeNull();
});
