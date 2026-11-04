import { test, expect } from "@playwright/test";

const purchase = { id: "12345678-1234-1234-1234-123456789012", pnr: "A".repeat(32), kind: "Ticket", tripId: 9, seatNumber: 12,
  companyName: "Örnek Firma", departureCity: "İstanbul", arrivalCity: "Ankara", departureTime: "2099-11-10T08:00:00Z", arrivalTime: "2099-11-10T14:00:00Z",
  passengerName: "Satın Alınırken Kaydedilen Ad", passengerEmail: "purchase@example.com", passengerPhone: "+905321234567", passengerSnapshotSource: "Checkout",
  passengerCapturedAt: "2026-11-01T08:00:00Z", paidAt: "2026-11-01T08:00:00Z", retrievedAt: "2026-11-04T08:00:00Z", cancelDeadline: "2099-11-10T06:00:00Z",
  holdExpiresAt: null, amount: 600, currency: "TRY", isTest: true, paymentStatus: "Paid", ticketStatus: "Active", refundStatus: "None", refundAmount: 0,
  canCancel: true, cancellationReason: null, refundStatusLabel: "İade yok", refundDescription: "", cancelledAt: null, lastError: null, needsAttention: false, checkoutUrl: null };

async function session(page) {
  await page.addInitScript(() => { sessionStorage.setItem("travela_token", "snapshot-token"); sessionStorage.setItem("travela_user", JSON.stringify({ fullName: "Güncel Profil Adı", role: "Passenger" })); });
}

test("Bilet detayı değişen profil yerine sabit yolcu ve iletişim bilgilerini gösterir", async ({ page }) => {
  await session(page);
  await page.route(`**/api/tickets/${purchase.pnr}/communications`, route => route.fulfill({ json: { changes: [], notifications: [] } }));
  await page.route(`**/api/tickets/${purchase.pnr}`, route => route.fulfill({ json: purchase }));
  await page.goto(`/tickets/${purchase.pnr}`);
  await expect(page.locator(".ticket-document")).toContainText(purchase.passengerName);
  await expect(page.locator(".ticket-document")).toContainText(purchase.passengerEmail);
  await expect(page.locator(".ticket-document")).toContainText(purchase.passengerPhone);
  await expect(page.locator(".ticket-document")).not.toContainText("Güncel Profil Adı");
});

test("Değiştirilen bilet yeni kalkışa kadar tam iade hakkını ve teslimat geçmişini gösterir", async ({ page }) => {
  await session(page);
  const journey = { departureTime: purchase.departureTime, busPlateNumber: "34 ABC 123" };
  await page.route(`**/api/tickets/${purchase.pnr}`, route => route.fulfill({ json: { ...purchase, changeCancellationUntil: purchase.departureTime, busPlateNumber: "34 XYZ 456" } }));
  await page.route(`**/api/tickets/${purchase.pnr}/communications`, route => route.fulfill({ json: { changes: [{ id: "change", createdAt: purchase.retrievedAt, reason: "Araç bakımından dolayı değiştirildi", beforeJson: JSON.stringify(journey), afterJson: JSON.stringify({ ...journey, busPlateNumber: "34 XYZ 456" }) }], notifications: [{ id: "n1", type: "TripChanged", channel: "Email", status: "Preview" }] } }));
  await page.goto(`/tickets/${purchase.pnr}`);
  await expect(page.getByText(/Sefer değişikliği nedeniyle .*tam iadeyle iptal hakkınız var/)).toBeVisible();
  const history = page.getByRole("region", { name: "Sefer değişikliği ve bildirim geçmişi" });
  await expect(history).toContainText("Araç bakımından dolayı değiştirildi"); await expect(history).toContainText("34 ABC 123"); await expect(history).toContainText("34 XYZ 456"); await expect(history).toContainText("Yerel önizleme");
  await expect(page.getByRole("button", { name: "Bileti İptal Et", exact: true })).toBeEnabled();
});

test("Ödeme ekranında sabitlenen yolcu bilgisi ödeme öncesinde görünür", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 1000 }); await session(page);
  await page.route(`**/api/tickets/orders/${purchase.id}`, route => route.fulfill({ json: { ...purchase, pnr: null, kind: "Hold", paymentStatus: "Pending", holdExpiresAt: "2099-11-10T07:00:00Z", checkoutUrl: `/payment/${purchase.id}` } }));
  await page.goto(`/payment/${purchase.id}`);
  const summary = page.getByRole("region", { name: "Bilette kayıtlı yolcu", exact: true });
  await expect(summary).toContainText(purchase.passengerName);
  await expect(summary).toContainText(purchase.passengerEmail);
  await expect(summary).toContainText("Profil güncellemeleri bu bilgileri değiştirmez");
  const size = await page.evaluate(() => ({ width: document.documentElement.scrollWidth, viewport: document.documentElement.clientWidth }));
  expect(size.width).toBeLessThanOrEqual(size.viewport + 1);
});
