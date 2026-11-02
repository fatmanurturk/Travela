import { test } from "node:test";
import assert from "node:assert/strict";
import { isIssuedTicket, paymentLabel, refundLabel, refundMessage, statusTone, safeCheckoutPath } from "../src/payments.ts";
import { passengerReturnTo } from "../src/reservations.ts";

test("İade talebi ile tamamlanmış iade birbirinden ayrılır", () => {
  assert.equal(refundLabel("Pending"), "İade bekleniyor");
  assert.equal(refundLabel("Succeeded"), "İade tamamlandı");
  assert.equal(refundLabel("Failed"), "İade tamamlanamadı");
  assert.equal(paymentLabel("Failed"), "Ödeme başarısız");
});
test("Bilet detay bağlantısı yalnızca ödenmiş ve PNR düzenlenmiş kayıtta sunulur", () => {
  assert.equal(isIssuedTicket({ pnr: "A".repeat(32), kind: "Ticket", paymentStatus: "Paid" }), true);
  for (const item of [
    { pnr: null, kind: "Hold", paymentStatus: "Pending" },
    { pnr: null, kind: "Hold", paymentStatus: "Failed" },
    { pnr: null, kind: "Legacy", paymentStatus: "Unpaid" },
    { pnr: "A".repeat(32), kind: "Ticket", paymentStatus: "Pending" },
  ]) assert.equal(isIssuedTicket(item), false);
  assert.equal(passengerReturnTo("/tickets/" + "A".repeat(32)), "/tickets/" + "A".repeat(32));
  assert.equal(passengerReturnTo("/tickets/../admin"), "/");
});
test("Ödeme ekranı ve giriş dönüşü yalnızca izin verilen yerel adrese gider", () => {
  const path = "/test-payment/12345678-1234-1234-1234-123456789012";
  assert.equal(safeCheckoutPath(path), path);
  assert.equal(passengerReturnTo(path), path);
  assert.equal(passengerReturnTo("/tickets"), "/tickets");
  for (const value of ["//example.com", "https://example.com", "/test-payment/../admin", "javascript:alert(1)"]) assert.equal(safeCheckoutPath(value), null);
});

test("Kısa iade mesajları ve ortak durum renkleri", () => {
 assert.equal(refundMessage("Pending"), "İadeniz bekleniyor.");
 assert.equal(refundMessage("Succeeded"), "İadeniz tamamlandı.");
 assert.equal(refundMessage("Failed"), "İade tamamlanamadı.");
 assert.equal(refundMessage("None"), "");
 assert.equal(statusTone("Paid"), statusTone("Succeeded"));
 assert.equal(statusTone("Failed"), statusTone("Cancelled"));
 assert.notEqual(statusTone("Pending"), statusTone("Succeeded"));
});
